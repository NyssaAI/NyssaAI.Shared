// Responsibility: Provide SQLite connection execution adapter using better-sqlite3.
// Public interface:
// `-- SqliteConnectionAdapter
//     +-- beginTransaction(signal)
//     +-- close()
//     +-- commit(signal)
//     +-- dialect
//     +-- executeNonQuery(sql, params, _timeoutSeconds, signal)
//     +-- executeQuery(sql, params, _timeoutSeconds, signal)
//     +-- new(db)
//     `-- rollback(signal)

import type Database from "better-sqlite3";
import type { DbDialect } from "../DbDialect.js";
import { DbRecordReader } from "../commands/DbRecordReader.js";

export class SqliteConnectionAdapter {
  public readonly dialect: DbDialect = "sqlite";
  private db: Database.Database | null;

  public constructor(db: Database.Database) {
    this.db = db;
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
    const stmt = this.db!.prepare(sql);
    const bindData = SqliteConnectionAdapter.normalizeParams(params);
    const result = bindData !== undefined ? stmt.run(bindData) : stmt.run();
    return result.changes;
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
    const stmt = this.db!.prepare(sql);
    const bindData = SqliteConnectionAdapter.normalizeParams(params);
    const rows = (bindData !== undefined ? stmt.all(bindData) : stmt.all()) as Record<string, unknown>[];
    const columns = stmt.columns().map((c) => c.name);
    return new DbRecordReader(rows, columns);
  }

  public async beginTransaction(signal?: AbortSignal): Promise<void> {
    this.ensureOpen();
    if (signal?.aborted) {
      throw signal.reason ?? new Error("Operation aborted.");
    }
    this.db!.exec("BEGIN IMMEDIATE");
  }

  public async commit(signal?: AbortSignal): Promise<void> {
    this.ensureOpen();
    if (signal?.aborted) {
      throw signal.reason ?? new Error("Operation aborted.");
    }
    this.db!.exec("COMMIT");
  }

  public async rollback(signal?: AbortSignal): Promise<void> {
    this.ensureOpen();
    if (signal?.aborted) {
      throw signal.reason ?? new Error("Operation aborted.");
    }
    this.db!.exec("ROLLBACK");
  }

  public async close(): Promise<void> {
    if (this.db) {
      try {
        if (this.db.open) {
          this.db.close();
        }
      } finally {
        this.db = null;
      }
    }
  }

  private ensureOpen(): void {
    if (!this.db || !this.db.open) {
      throw new Error("SQLite connection is closed or disposed.");
    }
  }

  private static normalizeParams(params: unknown): unknown {
    if (params === undefined || params === null) {
      return undefined;
    }
    if (Array.isArray(params) || typeof params === "object") {
      return params;
    }
    return [params];
  }
}
