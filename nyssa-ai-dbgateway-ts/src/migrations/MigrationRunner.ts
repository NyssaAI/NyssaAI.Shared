// Responsibility: Execute and verify schema migrations across SQLite and PostgreSQL providers.
// Public interface:
// `-- MigrationRunner
//     +-- migrate(release, signal)
//     +-- migrateTo(targetVersion, signal)
//     +-- migrateUp(signal)
//     +-- new(provider, catalog)
//     +-- status(signal)
//     `-- verify(strict, signal)

import type { DbProvider } from "../providers/DbProvider.js";
import type { DbConnectionAdapter } from "../providers/DbConnectionAdapter.js";
import type { DbDialect } from "../DbDialect.js";
import { MigrationCatalog } from "./MigrationCatalog.js";
import { MigrationReport } from "./MigrationReport.js";
import type { MigrationStep } from "./MigrationStep.js";

const POSTGRES_ADVISORY_LOCK_ID = "564535375002";
const MIGRATIONS_TABLE = "nyssa_dbgateway_migrations";

export class MigrationRunner {
  private readonly provider: DbProvider;
  private readonly catalog: MigrationCatalog | undefined;

  public constructor(provider: DbProvider, catalog?: MigrationCatalog) {
    if (!provider) {
      throw new Error("DbProvider is required.");
    }
    this.provider = provider;
    this.catalog = catalog;
  }

  public async migrate(release: string, signal?: AbortSignal): Promise<MigrationReport> {
    if (!this.catalog) {
      throw new Error("No migration catalog is registered.");
    }
    const target = this.catalog.resolve(release);
    return this.executeMigration(target, signal);
  }

  public async migrateUp(signal?: AbortSignal): Promise<MigrationReport> {
    if (!this.catalog) {
      throw new Error("No migration catalog is registered.");
    }
    const target = this.catalog.steps.length;
    return this.executeMigration(target, signal);
  }

  public async migrateTo(
    targetVersion: string | number,
    signal?: AbortSignal
  ): Promise<MigrationReport> {
    if (!this.catalog) {
      throw new Error("No migration catalog is registered.");
    }
    const target =
      typeof targetVersion === "number"
        ? targetVersion
        : this.catalog.resolve(targetVersion);
    return this.executeMigration(target, signal);
  }

  public async verify(strict = false, signal?: AbortSignal): Promise<boolean> {
    if (!this.catalog) {
      throw new Error("No migration catalog is registered.");
    }
    const conn = await this.provider.openConnection(signal);
    try {
      await this.ensureMigrationsTable(conn, signal);
      const current = await this.readHistory(conn, this.provider.dialect, signal);
      if (strict && current !== this.catalog.steps.length) {
        return false;
      }
      return true;
    } catch {
      return false;
    } finally {
      await conn.close();
    }
  }

  public async status(
    signal?: AbortSignal
  ): Promise<{ readonly currentStep: number; readonly targetStep: number }> {
    if (!this.catalog) {
      throw new Error("No migration catalog is registered.");
    }
    const conn = await this.provider.openConnection(signal);
    try {
      await this.ensureMigrationsTable(conn, signal);
      const current = await this.readHistory(conn, this.provider.dialect, signal);
      return {
        currentStep: current,
        targetStep: this.catalog.steps.length
      };
    } finally {
      await conn.close();
    }
  }

  private async executeMigration(
    target: number,
    signal?: AbortSignal
  ): Promise<MigrationReport> {
    const catalog = this.catalog!;
    const dialect = this.provider.dialect;
    const conn = await this.provider.openConnection(signal);
    let inTransaction = false;

    try {
      await conn.beginTransaction(signal);
      inTransaction = true;

      if (dialect === "postgres") {
        const lockReader = await conn.executeQuery(
          `SELECT pg_try_advisory_xact_lock(${POSTGRES_ADVISORY_LOCK_ID})`,
          [],
          catalog.commandTimeoutSeconds,
          signal
        );
        if (!(await lockReader.next(signal)) || lockReader.getValue(0) !== true) {
          throw new Error("Another migration runner owns this database.");
        }
        const stringSetting = await conn.executeQuery(
          "SHOW standard_conforming_strings",
          [],
          catalog.commandTimeoutSeconds,
          signal
        );
        if (
          !(await stringSetting.next(signal)) ||
          stringSetting.getString(0) !== "on"
        ) {
          throw new Error("Migrations require PostgreSQL standard_conforming_strings=on.");
        }
      }

      await this.ensureMigrationsTable(conn, signal);
      const current = await this.readHistory(conn, dialect, signal);

      if (current > target) {
        for (let num = target; num < current; num++) {
          const step = catalog.steps[num];
          if (!step || !step.getDownSql(dialect)) {
            throw new Error("The requested downgrade includes an irreversible migration.");
          }
        }
      }

      let applied = 0;
      if (current < target) {
        for (let number = current + 1; number <= target; number++) {
          const step = catalog.steps[number - 1]!;
          const timeout = step.commandTimeoutSeconds ?? catalog.commandTimeoutSeconds;
          await conn.executeNonQuery(step.getUpSql(dialect), [], timeout, signal);
          const fingerprint = MigrationCatalog.fingerprint(step, dialect);
          const insertSql =
            dialect === "sqlite"
              ? `INSERT INTO ${MIGRATIONS_TABLE} (number, fingerprint) VALUES (${number}, '${fingerprint}')`
              : `INSERT INTO ${MIGRATIONS_TABLE} (number, fingerprint) VALUES ($1, $2)`;
          const insertParams = dialect === "sqlite" ? [] : [number, fingerprint];
          await conn.executeNonQuery(insertSql, insertParams, catalog.commandTimeoutSeconds, signal);
          applied++;
        }
      } else if (current > target) {
        for (let number = current; number > target; number--) {
          const step = catalog.steps[number - 1]!;
          const timeout = step.commandTimeoutSeconds ?? catalog.commandTimeoutSeconds;
          await conn.executeNonQuery(step.getDownSql(dialect)!, [], timeout, signal);
          const deleteSql =
            dialect === "sqlite"
              ? `DELETE FROM ${MIGRATIONS_TABLE} WHERE number = ${number}`
              : `DELETE FROM ${MIGRATIONS_TABLE} WHERE number = $1`;
          const deleteParams = dialect === "sqlite" ? [] : [number];
          await conn.executeNonQuery(deleteSql, deleteParams, catalog.commandTimeoutSeconds, signal);
          applied++;
        }
      }

      await conn.commit(signal);
      inTransaction = false;

      return new MigrationReport({
        fromNumber: current,
        toNumber: target,
        appliedCount: applied
      });
    } catch (error) {
      if (inTransaction) {
        try {
          await conn.rollback();
        } catch {
          // ignore rollback failure on error
        }
      }
      throw error;
    } finally {
      await conn.close();
    }
  }

  private async ensureMigrationsTable(
    conn: DbConnectionAdapter,
    signal?: AbortSignal
  ): Promise<void> {
    const sql = `CREATE TABLE IF NOT EXISTS ${MIGRATIONS_TABLE} (number BIGINT PRIMARY KEY, fingerprint TEXT NOT NULL)`;
    await conn.executeNonQuery(sql, [], this.catalog?.commandTimeoutSeconds ?? 30, signal);
  }

  private async readHistory(
    conn: DbConnectionAdapter,
    dialect: DbDialect,
    signal?: AbortSignal
  ): Promise<number> {
    const catalog = this.catalog!;
    const reader = await conn.executeQuery(
      `SELECT number, fingerprint FROM ${MIGRATIONS_TABLE} ORDER BY number`,
      [],
      catalog.commandTimeoutSeconds,
      signal
    );

    let current = 0;
    while (await reader.next(signal)) {
      const number = reader.getInt32(0);
      const fingerprint = reader.getString(1);

      if (
        number !== current + 1 ||
        number > catalog.steps.length
      ) {
        throw new Error("Migration history does not match the compiled catalog prefix.");
      }

      const expectedStep = catalog.steps[number - 1]!;
      const expectedFingerprint = MigrationCatalog.fingerprint(expectedStep, dialect);
      if (fingerprint !== expectedFingerprint) {
        throw new Error("Migration history does not match the compiled catalog prefix.");
      }

      current = number;
    }
    return current;
  }
}
