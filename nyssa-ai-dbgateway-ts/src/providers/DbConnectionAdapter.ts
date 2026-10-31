// Responsibility: Define execution contract for an open underlying database connection.
// Public interface:
// `-- DbConnectionAdapter
//     +-- beginTransaction(signal)
//     +-- close()
//     +-- commit(signal)
//     +-- dialect
//     +-- executeNonQuery(sql, params, timeoutSeconds, signal)
//     +-- executeQuery(sql, params, timeoutSeconds, signal)
//     `-- rollback(signal)

import type { DbDialect } from "../DbDialect.js";
import type { DbRecordReader } from "../commands/DbRecordReader.js";

export interface DbConnectionAdapter {
  readonly dialect: DbDialect;
  executeNonQuery(
    sql: string,
    params: unknown,
    timeoutSeconds?: number,
    signal?: AbortSignal
  ): Promise<number>;
  executeQuery(
    sql: string,
    params: unknown,
    timeoutSeconds?: number,
    signal?: AbortSignal
  ): Promise<DbRecordReader>;
  beginTransaction(signal?: AbortSignal): Promise<void>;
  commit(signal?: AbortSignal): Promise<void>;
  rollback(signal?: AbortSignal): Promise<void>;
  close(): Promise<void>;
}
