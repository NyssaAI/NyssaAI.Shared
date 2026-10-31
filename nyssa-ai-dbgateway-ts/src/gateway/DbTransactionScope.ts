// Responsibility: Manage a single database transaction with sequential execution and failure latching.
// Public interface:
// `-- DbTransactionScope
//     +-- [Symbol.asyncDispose]()
//     +-- commit(signal)
//     +-- databaseProvider
//     +-- dialect
//     +-- execute(command, signal)
//     +-- execute(commandOrSql, paramsOrSignal, signal)
//     +-- execute(sql, params, signal)
//     +-- executeCommand(command, signal)
//     +-- new(connection, databaseProvider, dialect, commandTimeoutSeconds, logger)
//     +-- query(query, signal)
//     +-- query(queryOrSql, paramsOrSignal, signal)
//     +-- query(sql, params, signal)
//     +-- querySingle(query, signal)
//     +-- querySingle(queryOrSql, paramsOrSignal, signal)
//     +-- querySingle(sql, params, signal)
//     +-- recordFailure(error)
//     +-- rollback(signal)
//     `-- scalar(sql, params, signal)

import type { DbDialect } from "../DbDialect.js";
import type { DbConnectionAdapter } from "../providers/DbConnectionAdapter.js";
import type { DbNonQuery } from "../commands/DbNonQuery.js";
import type { DbCommand } from "../commands/DbCommand.js";
import type { DbQuery } from "../commands/DbQuery.js";
import { DbCommandExecutor } from "./DbCommandExecutor.js";
import type { DbGatewayLogger } from "../logging/DbGatewayLogger.js";

export class DbTransactionScope {
  public readonly databaseProvider: string;
  public readonly dialect: DbDialect;
  private readonly connection: DbConnectionAdapter;
  private readonly commandTimeoutSeconds: number;
  private readonly logger: DbGatewayLogger | undefined;

  private isBusy = false;
  private failure: Error | null = null;
  private isCompleted = false;
  private isDisposed = false;

  public constructor(
    connection: DbConnectionAdapter,
    databaseProvider: string,
    dialect: DbDialect,
    commandTimeoutSeconds: number,
    logger?: DbGatewayLogger
  ) {
    this.connection = connection;
    this.databaseProvider = databaseProvider;
    this.dialect = dialect;
    this.commandTimeoutSeconds = commandTimeoutSeconds;
    this.logger = logger;
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
    await this.enter();
    try {
      this.checkReady(signal, true);
      let result: number | TResult;
      if (typeof commandOrSql === "string") {
        const params = Array.isArray(paramsOrSignal) || (typeof paramsOrSignal === "object" && !(paramsOrSignal instanceof AbortSignal))
          ? (paramsOrSignal as Record<string, unknown> | readonly unknown[])
          : undefined;
        const actualSignal = paramsOrSignal instanceof AbortSignal ? paramsOrSignal : signal;
        result = await DbCommandExecutor.executeAdHoc(
          this.connection,
          this.dialect,
          this.commandTimeoutSeconds,
          commandOrSql,
          params,
          actualSignal
        );
      } else if ("readResult" in commandOrSql) {
        result = await DbCommandExecutor.executeCommand(
          this.connection,
          this.dialect,
          this.commandTimeoutSeconds,
          commandOrSql,
          signal
        );
      } else {
        result = await DbCommandExecutor.executeNonQuery(
          this.connection,
          this.dialect,
          this.commandTimeoutSeconds,
          commandOrSql,
          signal
        );
      }
      this.logger?.debug("transaction.execute", {
        elapsedMs: Math.round(performance.now() - started),
        outcome: "success"
      });
      return result;
    } catch (error) {
      this.recordFailure(error instanceof Error ? error : new Error(String(error)));
      this.logger?.error("transaction.execute", {
        elapsedMs: Math.round(performance.now() - started),
        outcome: "failure"
      }, error);
      throw error;
    } finally {
      this.isBusy = false;
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
    await this.enter();
    try {
      this.checkReady(signal, true);
      const result = await DbCommandExecutor.executeScalar<T>(
        this.connection,
        this.dialect,
        this.commandTimeoutSeconds,
        sql,
        params,
        signal
      );
      this.logger?.debug("transaction.scalar", {
        elapsedMs: Math.round(performance.now() - started),
        outcome: "success"
      });
      return result;
    } catch (error) {
      this.recordFailure(error instanceof Error ? error : new Error(String(error)));
      this.logger?.error("transaction.scalar", {
        elapsedMs: Math.round(performance.now() - started),
        outcome: "failure"
      }, error);
      throw error;
    } finally {
      this.isBusy = false;
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
    await this.enter();
    try {
      this.checkReady(signal, true);
      let results: readonly TRow[];
      if (typeof queryOrSql === "string") {
        const params = Array.isArray(paramsOrSignal) || (typeof paramsOrSignal === "object" && !(paramsOrSignal instanceof AbortSignal))
          ? (paramsOrSignal as Record<string, unknown> | readonly unknown[])
          : undefined;
        const actualSignal = paramsOrSignal instanceof AbortSignal ? paramsOrSignal : signal;
        results = await DbCommandExecutor.executeAdHocQuery<TRow>(
          this.connection,
          this.dialect,
          this.commandTimeoutSeconds,
          queryOrSql,
          params,
          actualSignal
        );
      } else {
        results = await DbCommandExecutor.executeQuery(
          this.connection,
          this.dialect,
          this.commandTimeoutSeconds,
          queryOrSql,
          signal
        );
      }
      this.logger?.debug("transaction.query", {
        elapsedMs: Math.round(performance.now() - started),
        rowCount: results.length,
        outcome: "success"
      });
      return results;
    } catch (error) {
      this.recordFailure(error instanceof Error ? error : new Error(String(error)));
      this.logger?.error("transaction.query", {
        elapsedMs: Math.round(performance.now() - started),
        outcome: "failure"
      }, error);
      throw error;
    } finally {
      this.isBusy = false;
    }
  }

  public async commit(signal?: AbortSignal): Promise<void> {
    await this.enter();
    try {
      this.checkReady(signal, true);
      await this.connection.commit(signal);
      this.isCompleted = true;
    } catch (error) {
      this.recordFailure(error instanceof Error ? error : new Error(String(error)));
      throw error;
    } finally {
      this.isBusy = false;
    }
  }

  public async rollback(signal?: AbortSignal): Promise<void> {
    await this.enter();
    try {
      this.checkReady(signal, false);
      await this.connection.rollback(signal);
      this.isCompleted = true;
    } catch (error) {
      this.recordFailure(error instanceof Error ? error : new Error(String(error)));
      throw error;
    } finally {
      this.isBusy = false;
    }
  }

  public recordFailure(error: Error): void {
    if (!this.failure) {
      this.failure = error;
    }
  }

  public async [Symbol.asyncDispose](): Promise<void> {
    if (this.isDisposed) {
      return;
    }
    this.isDisposed = true;
    try {
      if (!this.isCompleted) {
        try {
          await this.connection.rollback();
        } catch {
          // Swallow rollback exceptions on disposal
        }
      }
    } finally {
      await this.connection.close();
    }
  }

  private async enter(): Promise<void> {
    if (this.isBusy) {
      const err = new Error("A transaction scope supports only one operation at a time.");
      this.recordFailure(err);
      throw err;
    }
    this.isBusy = true;
  }

  private checkReady(signal?: AbortSignal, includeFailure?: boolean): void {
    if (this.isDisposed) {
      throw new Error("Transaction scope is already disposed.");
    }
    if (this.isCompleted) {
      throw new Error("The transaction is already completed.");
    }
    if (includeFailure && this.failure) {
      throw new Error("The transaction failed and must be rolled back.");
    }
    if (signal?.aborted) {
      throw signal.reason ?? new Error("Operation aborted.");
    }
  }
}
