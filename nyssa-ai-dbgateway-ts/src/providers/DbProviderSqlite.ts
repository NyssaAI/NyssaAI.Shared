// Responsibility: Provide SQLite connections and command execution using better-sqlite3.
// Public interface:
// `-- DbProviderSqlite
//     +-- databaseProvider
//     +-- dialect
//     +-- dispose()
//     +-- openConnection(signal)
//     +-- prepare(settings)
//     `-- resolveDatabasePath(connectionString)

import fs from "node:fs";
import path from "node:path";
import Database from "better-sqlite3";
import type { DbDialect } from "../DbDialect.js";
import type { DbProvider } from "./DbProvider.js";
import type { DbConnectionAdapter } from "./DbConnectionAdapter.js";
import { SqliteConnectionAdapter } from "./SqliteConnectionAdapter.js";
import {
  type DbGatewayConfiguration,
  validateDbGatewayConfiguration
} from "../configuration/DbGatewayConfiguration.js";

export class DbProviderSqlite {
  public readonly databaseProvider = "sqlite";
  public readonly dialect: DbDialect = "sqlite";
  private readonly dbPath: string;
  private isDisposed = false;

  private constructor(dbPath: string) {
    this.dbPath = dbPath;
  }

  public static prepare(settings: DbGatewayConfiguration): () => DbProvider {
    validateDbGatewayConfiguration(settings);
    const dbPath = DbProviderSqlite.resolveDatabasePath(settings.connectionString);
    return () => new DbProviderSqlite(dbPath);
  }

  public async openConnection(signal?: AbortSignal): Promise<DbConnectionAdapter> {
    if (this.isDisposed) {
      throw new Error("DbProviderSqlite is disposed.");
    }
    if (signal?.aborted) {
      throw signal.reason ?? new Error("Connection opening was aborted.");
    }
    if (this.dbPath !== ":memory:") {
      const dir = path.dirname(this.dbPath);
      if (dir && !fs.existsSync(dir)) {
        fs.mkdirSync(dir, { recursive: true });
      }
    }
    const db = new Database(this.dbPath);
    db.pragma("foreign_keys = ON");
    return new SqliteConnectionAdapter(db);
  }

  public async dispose(): Promise<void> {
    this.isDisposed = true;
  }

  public static resolveDatabasePath(connectionString: string): string {
    const trimmed = connectionString.trim();
    if (trimmed === ":memory:" || trimmed.toLowerCase().startsWith("data source=:memory:")) {
      return ":memory:";
    }
    const match = /data source=([^;]+)/i.exec(trimmed);
    if (match && match[1]) {
      const parsed = match[1].trim();
      if (!parsed) {
        throw new Error("Invalid SQLite connection string: empty Data Source.");
      }
      return parsed;
    }
    return trimmed;
  }
}
