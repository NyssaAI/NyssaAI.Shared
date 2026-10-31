// Responsibility: Provide PostgreSQL pooled connections and command execution using pg.
// Public interface:
// `-- DbProviderPostgres
//     +-- databaseProvider
//     +-- dialect
//     +-- dispose()
//     +-- openConnection(signal)
//     +-- parseConnectionString(connectionString)
//     `-- prepare(settings)

import pg from "pg";
import type { DbDialect } from "../DbDialect.js";
import type { DbProvider } from "./DbProvider.js";
import type { DbConnectionAdapter } from "./DbConnectionAdapter.js";
import { PostgresConnectionAdapter } from "./PostgresConnectionAdapter.js";
import {
  type DbGatewayConfiguration,
  validateDbGatewayConfiguration
} from "../configuration/DbGatewayConfiguration.js";

export class DbProviderPostgres {
  public readonly databaseProvider = "postgresql";
  public readonly dialect: DbDialect = "postgres";
  private readonly poolConfig: pg.PoolConfig;
  private pool: pg.Pool | null = null;
  private isDisposed = false;

  private constructor(poolConfig: pg.PoolConfig) {
    this.poolConfig = poolConfig;
  }

  public static prepare(settings: DbGatewayConfiguration): () => DbProvider {
    validateDbGatewayConfiguration(settings);
    const poolConfig = DbProviderPostgres.parseConnectionString(settings.connectionString);
    return () => new DbProviderPostgres(poolConfig);
  }

  public async openConnection(signal?: AbortSignal): Promise<DbConnectionAdapter> {
    if (this.isDisposed) {
      throw new Error("DbProviderPostgres is disposed.");
    }
    if (signal?.aborted) {
      throw signal.reason ?? new Error("Connection opening was aborted.");
    }
    if (!this.pool) {
      this.pool = new pg.Pool(this.poolConfig);
    }
    const client = await this.pool.connect();
    return new PostgresConnectionAdapter(client);
  }

  public async dispose(): Promise<void> {
    this.isDisposed = true;
    if (this.pool) {
      const current = this.pool;
      this.pool = null;
      await current.end();
    }
  }

  public static parseConnectionString(connectionString: string): pg.PoolConfig {
    const trimmed = connectionString.trim();
    if (!trimmed) {
      throw new Error("PostgreSQL connection string cannot be empty.");
    }
    if (trimmed.startsWith("postgres://") || trimmed.startsWith("postgresql://")) {
      return { connectionString: trimmed };
    }

    const config: pg.PoolConfig = {};
    const parts = trimmed.split(";");
    for (const part of parts) {
      const eqIdx = part.indexOf("=");
      if (eqIdx === -1) {
        continue;
      }
      const key = part.slice(0, eqIdx).trim().toLowerCase();
      const val = part.slice(eqIdx + 1).trim();
      switch (key) {
        case "host":
        case "server":
          config.host = val;
          break;
        case "port":
          config.port = parseInt(val, 10);
          break;
        case "database":
        case "db":
          config.database = val;
          break;
        case "username":
        case "user id":
        case "user":
        case "uid":
          config.user = val;
          break;
        case "password":
        case "pwd":
          config.password = val;
          break;
        case "command timeout":
        case "timeout":
          config.connectionTimeoutMillis = parseInt(val, 10) * 1000;
          break;
        case "minpoolsize":
          config.min = parseInt(val, 10);
          break;
        case "maxpoolsize":
          config.max = parseInt(val, 10);
          break;
        case "sslmode":
        case "ssl mode":
          if (val.toLowerCase() === "require") {
            config.ssl = true;
          } else if (val.toLowerCase() === "disable") {
            config.ssl = false;
          }
          break;
      }
    }
    return config;
  }
}
