// Responsibility: Use-case test schema migration execution, idempotency, downgrades, and history verification.
// Public interface:
// (none)

import { describe, it, expect, beforeEach, afterEach } from "vitest";
import fs from "node:fs";
import path from "node:path";
import os from "node:os";
import { DbProviderSqlite } from "../../src/providers/DbProviderSqlite.js";
import { MigrationStep } from "../../src/migrations/MigrationStep.js";
import { MigrationReleasePin } from "../../src/migrations/MigrationReleasePin.js";
import { MigrationCatalog } from "../../src/migrations/MigrationCatalog.js";
import { MigrationRunner } from "../../src/migrations/MigrationRunner.js";

describe("DbGatewayMigration Use Cases", () => {
  let tempDir: string;
  let dbPath: string;
  let provider: DbProviderSqlite;
  let catalog: MigrationCatalog;
  let runner: MigrationRunner;

  beforeEach(() => {
    tempDir = fs.mkdtempSync(path.join(os.tmpdir(), "nyssa-migration-uc-"));
    dbPath = path.join(tempDir, "migration_test.db");
    const factory = DbProviderSqlite.prepare({
      databaseProvider: "sqlite",
      connectionString: `Data Source=${dbPath}`,
      commandTimeoutSeconds: 15
    });
    provider = factory() as DbProviderSqlite;

    catalog = MigrationCatalog.create(
      [
        MigrationStep.paired(
          1,
          "CREATE TABLE items (id TEXT PRIMARY KEY, active INTEGER NOT NULL);",
          "CREATE TABLE items (id UUID PRIMARY KEY, active BOOLEAN NOT NULL);",
          "DROP TABLE items;",
          "DROP TABLE items;"
        ),
        MigrationStep.universal(
          2,
          "ALTER TABLE items ADD COLUMN note TEXT;",
          "ALTER TABLE items DROP COLUMN note;"
        )
      ],
      [
        new MigrationReleasePin("0.0.0", 0),
        new MigrationReleasePin("1.0.0", 1),
        new MigrationReleasePin("1.1.0", 2)
      ]
    );

    runner = new MigrationRunner(provider, catalog);
  });

  afterEach(async () => {
    if (provider) {
      await provider.dispose();
    }
    if (fs.existsSync(tempDir)) {
      fs.rmSync(tempDir, { recursive: true, force: true });
    }
  });

  it("migrationRunner_upgradeIdempotentAndDowngrade_succeedsAcrossLifecycle", async () => {
    // Initial status
    const initialStatus = await runner.status();
    expect(initialStatus.currentStep).toBe(0);
    expect(initialStatus.targetStep).toBe(2);

    // 1. Migrate to 1.0.0
    const report1 = await runner.migrate("1.0.0");
    expect(report1.fromNumber).toBe(0);
    expect(report1.toNumber).toBe(1);
    expect(report1.appliedCount).toBe(1);

    const status1 = await runner.status();
    expect(status1.currentStep).toBe(1);

    // Verify history table contains correct fingerprint
    const conn = await provider.openConnection();
    try {
      const historyReader = await conn.executeQuery(
        "SELECT number, fingerprint FROM nyssa_dbgateway_migrations ORDER BY number"
      );
      expect(await historyReader.next()).toBe(true);
      expect(historyReader.getInt32("number")).toBe(1);
      const expectedFp1 = MigrationCatalog.fingerprint(catalog.steps[0]!, "sqlite");
      expect(historyReader.getString("fingerprint")).toBe(expectedFp1);
    } finally {
      await conn.close();
    }

    // 2. Migrate to 1.1.0
    const report2 = await runner.migrate("1.1.0");
    expect(report2.fromNumber).toBe(1);
    expect(report2.toNumber).toBe(2);
    expect(report2.appliedCount).toBe(1);

    const verified = await runner.verify(true);
    expect(verified).toBe(true);

    // 3. Idempotent re-run
    const reportIdempotent = await runner.migrate("1.1.0");
    expect(reportIdempotent.fromNumber).toBe(2);
    expect(reportIdempotent.toNumber).toBe(2);
    expect(reportIdempotent.appliedCount).toBe(0);

    // 4. Downgrade to 0.0.0
    const reportDowngrade = await runner.migrate("0.0.0");
    expect(reportDowngrade.fromNumber).toBe(2);
    expect(reportDowngrade.toNumber).toBe(0);
    expect(reportDowngrade.appliedCount).toBe(2);

    const finalStatus = await runner.status();
    expect(finalStatus.currentStep).toBe(0);
  });
});
