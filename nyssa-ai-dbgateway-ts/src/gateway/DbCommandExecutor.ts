// Responsibility: Execute typed commands and queries against an open database connection adapter.
// Public interface:
// `-- DbCommandExecutor
//     +-- executeAdHoc(connection, dialect, timeoutSeconds, sql, params, signal)
//     +-- executeAdHocQuery(connection, dialect, timeoutSeconds, sql, params, signal)
//     +-- executeCommand(connection, dialect, timeoutSeconds, command, signal)
//     +-- executeNonQuery(connection, dialect, timeoutSeconds, operation, signal)
//     +-- executeQuery(connection, dialect, timeoutSeconds, query, signal)
//     `-- executeScalar(connection, dialect, timeoutSeconds, sql, params, signal)

import type { DbDialect } from "../DbDialect.js";
import type { DbConnectionAdapter } from "../providers/DbConnectionAdapter.js";
import type { DbNonQuery } from "../commands/DbNonQuery.js";
import type { DbCommand } from "../commands/DbCommand.js";
import type { DbQuery } from "../commands/DbQuery.js";
import { DbParameterBinder } from "../commands/DbParameterBinder.js";
import { DbRecordReader } from "../commands/DbRecordReader.js";

export class DbCommandExecutor {
  public static async executeNonQuery(
    connection: DbConnectionAdapter,
    dialect: DbDialect,
    timeoutSeconds: number,
    operation: DbNonQuery,
    signal?: AbortSignal
  ): Promise<number> {
    if (!operation) {
      throw new Error("DbNonQuery operation is required.");
    }
    const sql = operation.getSql(dialect);
    if (!sql || !sql.trim()) {
      throw new Error("Command SQL statement cannot be empty.");
    }

    const binder = new DbParameterBinder();
    if (operation.bindParameters) {
      operation.bindParameters(binder, dialect);
    }

    if (dialect === "sqlite") {
      const params = binder.toSqliteParameters();
      return connection.executeNonQuery(sql, params, timeoutSeconds, signal);
    } else {
      const rewritten = binder.toPostgresParameters(sql);
      return connection.executeNonQuery(rewritten.sql, rewritten.positionalValues, timeoutSeconds, signal);
    }
  }

  public static async executeCommand<TResult>(
    connection: DbConnectionAdapter,
    dialect: DbDialect,
    timeoutSeconds: number,
    command: DbCommand<TResult>,
    signal?: AbortSignal
  ): Promise<TResult> {
    if (!command) {
      throw new Error("DbCommand operation is required.");
    }
    const sql = command.getSql(dialect);
    if (!sql || !sql.trim()) {
      throw new Error("Command SQL statement cannot be empty.");
    }

    const binder = new DbParameterBinder();
    command.bindParameters(binder, dialect);

    let reader: DbRecordReader;
    if (dialect === "sqlite") {
      const params = binder.toSqliteParameters();
      reader = await connection.executeQuery(sql, params, timeoutSeconds, signal);
    } else {
      const rewritten = binder.toPostgresParameters(sql);
      reader = await connection.executeQuery(rewritten.sql, rewritten.positionalValues, timeoutSeconds, signal);
    }

    return command.readResult(reader, signal);
  }

  public static async executeQuery<TRow>(
    connection: DbConnectionAdapter,
    dialect: DbDialect,
    timeoutSeconds: number,
    query: DbQuery<TRow>,
    signal?: AbortSignal
  ): Promise<readonly TRow[]> {
    if (!query) {
      throw new Error("DbQuery operation is required.");
    }
    const sql = query.getSql(dialect);
    if (!sql || !sql.trim()) {
      throw new Error("Query SQL statement cannot be empty.");
    }

    const binder = new DbParameterBinder();
    query.bindParameters(binder, dialect);

    let reader: DbRecordReader;
    if (dialect === "sqlite") {
      const params = binder.toSqliteParameters();
      reader = await connection.executeQuery(sql, params, timeoutSeconds, signal);
    } else {
      const rewritten = binder.toPostgresParameters(sql);
      reader = await connection.executeQuery(rewritten.sql, rewritten.positionalValues, timeoutSeconds, signal);
    }

    const results: TRow[] = [];
    while (await reader.next(signal)) {
      results.push(query.mapRow(reader));
    }
    return results;
  }

  public static async executeAdHoc(
    connection: DbConnectionAdapter,
    dialect: DbDialect,
    timeoutSeconds: number,
    sql: string,
    params?: Record<string, unknown> | readonly unknown[],
    signal?: AbortSignal
  ): Promise<number> {
    if (!sql || !sql.trim()) {
      throw new Error("SQL statement cannot be empty.");
    }
    const bound = DbCommandExecutor.prepareParams(dialect, sql, params);
    return connection.executeNonQuery(bound.sql, bound.params, timeoutSeconds, signal);
  }

  public static async executeScalar<T>(
    connection: DbConnectionAdapter,
    dialect: DbDialect,
    timeoutSeconds: number,
    sql: string,
    params?: Record<string, unknown> | readonly unknown[],
    signal?: AbortSignal
  ): Promise<T | null> {
    if (!sql || !sql.trim()) {
      throw new Error("SQL query cannot be empty.");
    }
    const bound = DbCommandExecutor.prepareParams(dialect, sql, params);
    const reader = await connection.executeQuery(bound.sql, bound.params, timeoutSeconds, signal);
    if (await reader.next(signal)) {
      if (reader.isNull(0)) {
        return null;
      }
      return reader.getValue(0) as T;
    }
    return null;
  }

  public static async executeAdHocQuery<TRow>(
    connection: DbConnectionAdapter,
    dialect: DbDialect,
    timeoutSeconds: number,
    sql: string,
    params?: Record<string, unknown> | readonly unknown[],
    signal?: AbortSignal
  ): Promise<readonly TRow[]> {
    if (!sql || !sql.trim()) {
      throw new Error("SQL query cannot be empty.");
    }
    const bound = DbCommandExecutor.prepareParams(dialect, sql, params);
    const reader = await connection.executeQuery(bound.sql, bound.params, timeoutSeconds, signal);
    const results: TRow[] = [];
    while (await reader.next(signal)) {
      const row: Record<string, unknown> = {};
      for (const col of reader.columnNames) {
        row[col] = reader.getValue(col);
      }
      results.push(row as TRow);
    }
    return results;
  }

  private static prepareParams(
    dialect: DbDialect,
    sql: string,
    params?: Record<string, unknown> | readonly unknown[]
  ): { sql: string; params: unknown } {
    if (!params) {
      return { sql, params: dialect === "sqlite" ? {} : [] };
    }
    if (Array.isArray(params)) {
      return { sql, params };
    }
    const binder = new DbParameterBinder();
    for (const [key, val] of Object.entries(params)) {
      binder.add(key, val);
    }
    if (dialect === "sqlite") {
      return { sql, params: binder.toSqliteParameters() };
    }
    const rewritten = binder.toPostgresParameters(sql);
    return { sql: rewritten.sql, params: rewritten.positionalValues };
  }
}
