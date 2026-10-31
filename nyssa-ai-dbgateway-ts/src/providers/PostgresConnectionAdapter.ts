// Responsibility: Provide PostgreSQL connection execution adapter using pg.PoolClient.
// Public interface:
// `-- PostgresConnectionAdapter
//     +-- beginTransaction(signal)
//     +-- close()
//     +-- commit(signal)
//     +-- dialect
//     +-- executeNonQuery(sql, params, _timeoutSeconds, signal)
//     +-- executeQuery(sql, params, _timeoutSeconds, signal)
//     +-- new(client)
//     `-- rollback(signal)

import type pg from "pg";
import type { DbDialect } from "../DbDialect.js";
import { DbRecordReader } from "../commands/DbRecordReader.js";

export class PostgresConnectionAdapter {
  public readonly dialect: DbDialect = "postgres";
  private client: pg.PoolClient | null;

  public constructor(client: pg.PoolClient) {
    this.client = client;
  }

  public async executeNonQuery(
    sql: string,
    params: unknown,
    _timeoutSeconds?: number,
    signal?: AbortSignal
  ): Promise<number> {
    this.ensureOpen();
    if (signal?.aborted) {
      throw signal.reason ?? new Error("Operation aborted.");
    }
    const values = Array.isArray(params) ? params : [];
    const result = await this.client!.query(sql, values);
    return result.rowCount ?? 0;
  }

  public async executeQuery(
    sql: string,
    params: unknown,
    _timeoutSeconds?: number,
    signal?: AbortSignal
  ): Promise<DbRecordReader> {
    this.ensureOpen();
    if (signal?.aborted) {
      throw signal.reason ?? new Error("Operation aborted.");
    }
    const values = Array.isArray(params) ? params : [];
    const result = await this.client!.query(sql, values);
    const columns = result.fields.map((f) => f.name);
    return new DbRecordReader(result.rows as Record<string, unknown>[], columns);
  }

  public async beginTransaction(signal?: AbortSignal): Promise<void> {
    this.ensureOpen();
    if (signal?.aborted) {
      throw signal.reason ?? new Error("Operation aborted.");
    }
    await this.client!.query("BEGIN");
  }

  public async commit(signal?: AbortSignal): Promise<void> {
    this.ensureOpen();
    if (signal?.aborted) {
      throw signal.reason ?? new Error("Operation aborted.");
    }
    await this.client!.query("COMMIT");
  }

  public async rollback(signal?: AbortSignal): Promise<void> {
    this.ensureOpen();
    if (signal?.aborted) {
      throw signal.reason ?? new Error("Operation aborted.");
    }
    await this.client!.query("ROLLBACK");
  }

  public async close(): Promise<void> {
    if (this.client) {
      try {
        this.client.release();
      } finally {
        this.client = null;
      }
    }
  }

  private ensureOpen(): void {
    if (!this.client) {
      throw new Error("PostgreSQL connection is closed or released.");
    }
  }
}
