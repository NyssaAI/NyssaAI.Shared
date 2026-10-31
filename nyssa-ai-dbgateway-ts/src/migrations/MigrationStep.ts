// Responsibility: Define a numbered schema migration step supporting universal or dialect-paired SQL.
// Public interface:
// `-- MigrationStep
//     +-- commandTimeoutSeconds
//     +-- getDownSql(dialect)
//     +-- getUpSql(dialect)
//     +-- new(params)
//     +-- number
//     +-- paired(number, sqliteUpSql, postgresUpSql, sqliteDownSql, postgresDownSql, commandTimeoutSeconds)
//     +-- postgresDownSql
//     +-- postgresUpSql
//     +-- sqliteDownSql
//     +-- sqliteUpSql
//     `-- universal(number, upSql, downSql, commandTimeoutSeconds)

import type { DbDialect } from "../DbDialect.js";

export class MigrationStep {
  public readonly number: number;
  public readonly sqliteUpSql: string;
  public readonly postgresUpSql: string;
  public readonly sqliteDownSql: string | undefined;
  public readonly postgresDownSql: string | undefined;
  public readonly commandTimeoutSeconds: number | undefined;

  public constructor(params: {
    readonly number: number;
    readonly sqliteUpSql: string;
    readonly postgresUpSql: string;
    readonly sqliteDownSql?: string | undefined;
    readonly postgresDownSql?: string | undefined;
    readonly commandTimeoutSeconds?: number | undefined;
  }) {
    this.number = params.number;
    this.sqliteUpSql = params.sqliteUpSql;
    this.postgresUpSql = params.postgresUpSql;
    this.sqliteDownSql = params.sqliteDownSql;
    this.postgresDownSql = params.postgresDownSql;
    this.commandTimeoutSeconds = params.commandTimeoutSeconds;
  }

  public static universal(
    number: number,
    upSql: string,
    downSql?: string,
    commandTimeoutSeconds?: number
  ): MigrationStep {
    return new MigrationStep({
      number,
      sqliteUpSql: upSql,
      postgresUpSql: upSql,
      sqliteDownSql: downSql,
      postgresDownSql: downSql,
      commandTimeoutSeconds
    });
  }

  public static paired(
    number: number,
    sqliteUpSql: string,
    postgresUpSql: string,
    sqliteDownSql?: string,
    postgresDownSql?: string,
    commandTimeoutSeconds?: number
  ): MigrationStep {
    return new MigrationStep({
      number,
      sqliteUpSql,
      postgresUpSql,
      sqliteDownSql,
      postgresDownSql,
      commandTimeoutSeconds
    });
  }

  public getUpSql(dialect: DbDialect): string {
    return dialect === "sqlite" ? this.sqliteUpSql : this.postgresUpSql;
  }

  public getDownSql(dialect: DbDialect): string | undefined {
    return dialect === "sqlite" ? this.sqliteDownSql : this.postgresDownSql;
  }
}
