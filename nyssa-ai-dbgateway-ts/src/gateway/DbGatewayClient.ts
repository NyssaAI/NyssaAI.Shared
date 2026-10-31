// Responsibility: Primary database gateway implementation coordinating providers, execution, and transactions.
// Public interface:
// `-- DbGatewayClient
//     +-- beginTransaction(signal)
//     +-- create(settings, options)
//     +-- databaseProvider
//     +-- dialect
//     +-- execute(command, signal)
//     +-- execute(commandOrSql, paramsOrSignal, signal)
//     +-- execute(sql, params, signal)
//     +-- executeCommand(command, signal)
//     +-- executeInTransaction(operation, signal)
//     +-- executeTransaction(work, signal)
//     +-- new(provider, commandTimeoutSeconds, logger)
//     +-- query(query, signal)
//     +-- query(queryOrSql, paramsOrSignal, signal)
//     +-- query(sql, params, signal)
//     +-- querySingle(query, signal)
//     +-- querySingle(queryOrSql, paramsOrSignal, signal)
//     +-- querySingle(sql, params, signal)
//     `-- scalar(sql, params, signal)

import type { DbDialect } from "../DbDialect.js";
import type { DbProvider } from "../providers/DbProvider.js";
import type { DbProviderRegistration } from "../providers/DbProviderRegistration.js";
import { DbProviderRegistry } from "../providers/DbProviderRegistry.js";
import type { DbNonQuery } from "../commands/DbNonQuery.js";
import type { DbCommand } from "../commands/DbCommand.js";
import type { DbQuery } from "../commands/DbQuery.js";
import type { DbTransaction } from "../DbTransaction.js";
import { DbTransactionScope } from "./DbTransactionScope.js";
import { DbCommandExecutor } from "./DbCommandExecutor.js";
import type { DbGatewayConfiguration } from "../configuration/DbGatewayConfiguration.js";
import type { DbGatewayLogger } from "../logging/DbGatewayLogger.js";

export class DbGatewayClient {
  public readonly databaseProvider: string;
  public readonly dialect: DbDialect;
  private readonly provider: DbProvider;
  private readonly commandTimeoutSeconds: number;
  private readonly logger: DbGatewayLogger | undefined;

  public constructor(
    provider: DbProvider,
    commandTimeoutSeconds = 30,
    logger?: DbGatewayLogger
  ) {
    if (!provider) {
      throw new Error("DbProvider is required.");
    }
    this.provider = provider;
    this.databaseProvider = provider.databaseProvider;
    this.dialect = provider.dialect;
    this.commandTimeoutSeconds = commandTimeoutSeconds >= 0 ? commandTimeoutSeconds : 30;
    this.logger = logger;
  }

  public static create(
    settings: DbGatewayConfiguration,
    options?: {
      readonly registrations?: readonly DbProviderRegistration[];
      readonly logger?: DbGatewayLogger;
    }
  ): DbGatewayClient {
    const factory = DbProviderRegistry.registerDatabaseProviders(settings, options?.registrations);
    const provider = factory();
    return new DbGatewayClient(provider, settings.commandTimeoutSeconds, options?.logger);
  }

  public async execute<TResult>(
    command: DbCommand<TResult>,
    signal?: AbortSignal
  ): Promise<TResult>;
  public async execute(
    command: DbNonQuery,
    signal?: AbortSignal
  ): Promise<number>;
  public async execute(
    sql: string,
    params?: Record<string, unknown> | readonly unknown[],
    signal?: AbortSignal
  ): Promise<number>;
  public async execute<TResult>(
    commandOrSql: DbNonQuery | DbCommand<TResult> | string,
    paramsOrSignal?: Record<string, unknown> | readonly unknown[] | AbortSignal,
    signal?: AbortSignal
  ): Promise<number | TResult> {
    const started = performance.now();
    const conn = await this.provider.openConnection(signal);
    try {
      let result: number | TResult;
      if (typeof commandOrSql === "string") {
        const params = Array.isArray(paramsOrSignal) || (typeof paramsOrSignal === "object" && !(paramsOrSignal instanceof AbortSignal))
          ? (paramsOrSignal as Record<string, unknown> | readonly unknown[])
          : undefined;
        const actualSignal = paramsOrSignal instanceof AbortSignal ? paramsOrSignal : signal;
        result = await DbCommandExecutor.executeAdHoc(
          conn,
          this.dialect,
          this.commandTimeoutSeconds,
          commandOrSql,
          params,
          actualSignal
        );
      } else if ("readResult" in commandOrSql) {
        result = await DbCommandExecutor.executeCommand(
          conn,
          this.dialect,
          this.commandTimeoutSeconds,
          commandOrSql,
          signal
        );
      } else {
        result = await DbCommandExecutor.executeNonQuery(
          conn,
          this.dialect,
          this.commandTimeoutSeconds,
          commandOrSql,
          signal
        );
      }
      this.logger?.debug("gateway.execute", {
        elapsedMs: Math.round(performance.now() - started),
        outcome: "success"
      });
      return result;
    } catch (error) {
      this.logger?.error("gateway.execute", {
        elapsedMs: Math.round(performance.now() - started),
        outcome: "failure"
      }, error);
      throw error;
    } finally {
      await conn.close();
    }
  }

  public async executeCommand<TResult>(
    command: DbCommand<TResult>,
    signal?: AbortSignal
  ): Promise<TResult> {
    return this.execute<TResult>(command, signal);
  }

  public async scalar<T>(
    sql: string,
    params?: Record<string, unknown> | readonly unknown[],
    signal?: AbortSignal
  ): Promise<T | null> {
    const started = performance.now();
    const conn = await this.provider.openConnection(signal);
    try {
      const result = await DbCommandExecutor.executeScalar<T>(
        conn,
        this.dialect,
        this.commandTimeoutSeconds,
        sql,
        params,
        signal
      );
      this.logger?.debug("gateway.scalar", {
        elapsedMs: Math.round(performance.now() - started),
        outcome: "success"
      });
      return result;
    } catch (error) {
      this.logger?.error("gateway.scalar", {
        elapsedMs: Math.round(performance.now() - started),
        outcome: "failure"
      }, error);
      throw error;
    } finally {
      await conn.close();
    }
  }

  public async querySingle<TRow>(
    query: DbQuery<TRow>,
    signal?: AbortSignal
  ): Promise<TRow | null>;
  public async querySingle<TRow>(
    sql: string,
    params?: Record<string, unknown> | readonly unknown[],
    signal?: AbortSignal
  ): Promise<TRow | null>;
  public async querySingle<TRow>(
    queryOrSql: DbQuery<TRow> | string,
    paramsOrSignal?: Record<string, unknown> | readonly unknown[] | AbortSignal,
    signal?: AbortSignal
  ): Promise<TRow | null> {
    const rows = await this.query(queryOrSql as string, paramsOrSignal as Record<string, unknown>, signal);
    const first = rows[0];
    return first !== undefined ? (first as TRow) : null;
  }

  public async query<TRow>(
    query: DbQuery<TRow>,
    signal?: AbortSignal
  ): Promise<readonly TRow[]>;
  public async query<TRow>(
    sql: string,
    params?: Record<string, unknown> | readonly unknown[],
    signal?: AbortSignal
  ): Promise<readonly TRow[]>;
  public async query<TRow>(
    queryOrSql: DbQuery<TRow> | string,
    paramsOrSignal?: Record<string, unknown> | readonly unknown[] | AbortSignal,
    signal?: AbortSignal
  ): Promise<readonly TRow[]> {
    const started = performance.now();
    const conn = await this.provider.openConnection(signal);
    try {
      let results: readonly TRow[];
      if (typeof queryOrSql === "string") {
        const params = Array.isArray(paramsOrSignal) || (typeof paramsOrSignal === "object" && !(paramsOrSignal instanceof AbortSignal))
          ? (paramsOrSignal as Record<string, unknown> | readonly unknown[])
          : undefined;
        const actualSignal = paramsOrSignal instanceof AbortSignal ? paramsOrSignal : signal;
        results = await DbCommandExecutor.executeAdHocQuery<TRow>(
          conn,
          this.dialect,
          this.commandTimeoutSeconds,
          queryOrSql,
          params,
          actualSignal
        );
      } else {
        results = await DbCommandExecutor.executeQuery(
          conn,
          this.dialect,
          this.commandTimeoutSeconds,
          queryOrSql,
          signal
        );
      }
      this.logger?.debug("gateway.query", {
        elapsedMs: Math.round(performance.now() - started),
        rowCount: results.length,
        outcome: "success"
      });
      return results;
    } catch (error) {
      this.logger?.error("gateway.query", {
        elapsedMs: Math.round(performance.now() - started),
        outcome: "failure"
      }, error);
      throw error;
    } finally {
      await conn.close();
    }
  }

  public async beginTransaction(signal?: AbortSignal): Promise<DbTransaction> {
    const conn = await this.provider.openConnection(signal);
    try {
      await conn.beginTransaction(signal);
      return new DbTransactionScope(
        conn,
        this.databaseProvider,
        this.dialect,
        this.commandTimeoutSeconds,
        this.logger
      );
    } catch (error) {
      await conn.close();
      throw error;
    }
  }

  public async executeInTransaction<T>(
    operation: (tx: DbTransaction) => Promise<T>,
    signal?: AbortSignal
  ): Promise<T> {
    if (!operation) {
      throw new Error("Transaction operation callback is required.");
    }
    const tx = (await this.beginTransaction(signal)) as DbTransactionScope;
    try {
      const result = await operation(tx);
      await tx.commit(signal);
      return result;
    } catch (error) {
      tx.recordFailure(error instanceof Error ? error : new Error(String(error)));
      throw error;
    } finally {
      await tx[Symbol.asyncDispose]();
    }
  }

  public async executeTransaction<T>(
    work: (tx: DbTransaction, signal?: AbortSignal) => Promise<T>,
    signal?: AbortSignal
  ): Promise<T> {
    return this.executeInTransaction((tx) => work(tx, signal), signal);
  }
}
