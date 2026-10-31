// Responsibility: Primary database gateway contract providing command execution and transactions.
// Public interface:
// `-- DbGateway
//     +-- beginTransaction(signal)
//     +-- databaseProvider
//     +-- dialect
//     +-- execute(command, signal)
//     +-- execute(commandOrSql, paramsOrSignal, signal)
//     +-- execute(sql, params, signal)
//     +-- executeCommand(command, signal)
//     +-- executeInTransaction(operation, signal)
//     +-- executeTransaction(work, signal)
//     +-- query(query, signal)
//     +-- query(queryOrSql, paramsOrSignal, signal)
//     +-- query(sql, params, signal)
//     +-- querySingle(query, signal)
//     +-- querySingle(queryOrSql, paramsOrSignal, signal)
//     +-- querySingle(sql, params, signal)
//     `-- scalar(sql, params, signal)

import type { DbDialect } from "./DbDialect.js";
import type { DbNonQuery } from "./commands/DbNonQuery.js";
import type { DbCommand } from "./commands/DbCommand.js";
import type { DbQuery } from "./commands/DbQuery.js";
import type { DbTransaction } from "./DbTransaction.js";

export interface DbGateway {
  readonly dialect: DbDialect;
  readonly databaseProvider: string;

  execute(command: DbNonQuery, signal?: AbortSignal): Promise<number>;
  execute<TResult>(command: DbCommand<TResult>, signal?: AbortSignal): Promise<TResult>;
  execute(
    sql: string,
    params?: Record<string, unknown> | readonly unknown[],
    signal?: AbortSignal
  ): Promise<number>;
  execute<TResult>(
    commandOrSql: DbNonQuery | DbCommand<TResult> | string,
    paramsOrSignal?: Record<string, unknown> | readonly unknown[] | AbortSignal,
    signal?: AbortSignal
  ): Promise<number | TResult>;

  executeCommand<TResult>(command: DbCommand<TResult>, signal?: AbortSignal): Promise<TResult>;

  scalar<T>(
    sql: string,
    params?: Record<string, unknown> | readonly unknown[],
    signal?: AbortSignal
  ): Promise<T | null>;

  querySingle<TRow>(query: DbQuery<TRow>, signal?: AbortSignal): Promise<TRow | null>;
  querySingle<TRow>(
    sql: string,
    params?: Record<string, unknown> | readonly unknown[],
    signal?: AbortSignal
  ): Promise<TRow | null>;
  querySingle<TRow>(
    queryOrSql: DbQuery<TRow> | string,
    paramsOrSignal?: Record<string, unknown> | readonly unknown[] | AbortSignal,
    signal?: AbortSignal
  ): Promise<TRow | null>;

  query<TRow>(query: DbQuery<TRow>, signal?: AbortSignal): Promise<readonly TRow[]>;
  query<TRow>(
    sql: string,
    params?: Record<string, unknown> | readonly unknown[],
    signal?: AbortSignal
  ): Promise<readonly TRow[]>;
  query<TRow>(
    queryOrSql: DbQuery<TRow> | string,
    paramsOrSignal?: Record<string, unknown> | readonly unknown[] | AbortSignal,
    signal?: AbortSignal
  ): Promise<readonly TRow[]>;

  beginTransaction(signal?: AbortSignal): Promise<DbTransaction>;
  executeInTransaction<T>(
    operation: (tx: DbTransaction) => Promise<T>,
    signal?: AbortSignal
  ): Promise<T>;
  executeTransaction<T>(
    work: (tx: DbTransaction, signal?: AbortSignal) => Promise<T>,
    signal?: AbortSignal
  ): Promise<T>;
}
