// Responsibility: Unit test migration step creation, catalog validation, and SHA-256 fingerprinting.
// Public interface:
// (none)

import { describe, it, expect } from "vitest";
import { MigrationStep } from "../../src/migrations/MigrationStep.js";
import { MigrationReleasePin } from "../../src/migrations/MigrationReleasePin.js";
import { MigrationCatalog } from "../../src/migrations/MigrationCatalog.js";

describe("MigrationCatalog and MigrationStep", () => {
  it("migrationStep_universalAndPaired_returnsDialectSpecificSql", () => {
    const universal = MigrationStep.universal(
      1,
      "CREATE TABLE universal_items (id TEXT PRIMARY KEY);",
      "DROP TABLE universal_items;"
    );
    expect(universal.getUpSql("sqlite")).toBe(universal.getUpSql("postgres"));
    expect(universal.getDownSql("sqlite")).toBe(universal.getDownSql("postgres"));

    const paired = MigrationStep.paired(
      2,
      "CREATE TABLE paired_items (id TEXT PRIMARY KEY);",
      "CREATE TABLE paired_items (id UUID PRIMARY KEY);"
    );
    expect(paired.getUpSql("sqlite")).toBe("CREATE TABLE paired_items (id TEXT PRIMARY KEY);");
    expect(paired.getUpSql("postgres")).toBe("CREATE TABLE paired_items (id UUID PRIMARY KEY);");
    expect(paired.getDownSql("sqlite")).toBeUndefined();
  });

  it("migrationCatalog_fingerprint_matchesFormulaIdentically", () => {
    const step = MigrationStep.paired(
      1,
      "CREATE TABLE items (id TEXT PRIMARY KEY, active INTEGER NOT NULL)",
      "CREATE TABLE items (id UUID PRIMARY KEY, active BOOLEAN NOT NULL)",
      "DROP TABLE items",
      "DROP TABLE items"
    );

    const sqliteFingerprint = MigrationCatalog.fingerprint(step, "sqlite");
    expect(sqliteFingerprint).toBe("a53e10b4c4dccd8f675475310dffabc2e66d5f805885948b015c5257cf5fabe1");
    expect(sqliteFingerprint).toMatch(/^[0-9a-f]{64}$/);
  });

  it("migrationCatalog_create_validatesContiguousStepsAndSemVerPins", () => {
    const catalog = MigrationCatalog.create(
      [
        MigrationStep.universal(1, "CREATE TABLE a (id INT);"),
        MigrationStep.universal(2, "CREATE TABLE b (id INT);")
      ],
      [
        new MigrationReleasePin("1.0.0", 1),
        new MigrationReleasePin("1.1.0", 2),
        new MigrationReleasePin("0.0.0", 0)
      ]
    );

    expect(catalog.steps.length).toBe(2);
    expect(catalog.resolve("1.0.0")).toBe(1);
    expect(catalog.resolve("1.1.0")).toBe(2);
    expect(catalog.resolve("0.0.0")).toBe(0);
    expect(() => catalog.resolve("2.0.0")).toThrow();
  });

  it("migrationCatalog_invalidPinsOrSteps_throwsErrors", () => {
    // Non-contiguous steps
    expect(() =>
      MigrationCatalog.create(
        [MigrationStep.universal(2, "CREATE TABLE a (id INT);")],
        [new MigrationReleasePin("1.0.0", 2)]
      )
    ).toThrow("Migration steps must be contiguous starting at 1.");

    // Invalid SemVer pin
    expect(() =>
      MigrationCatalog.create(
        [MigrationStep.universal(1, "CREATE TABLE a (id INT);")],
        [new MigrationReleasePin("v1.0", 1)]
      )
    ).toThrow("Migration release pins must be exact full SemVer strings.");

    // Step out of bounds
    expect(() =>
      MigrationCatalog.create(
        [MigrationStep.universal(1, "CREATE TABLE a (id INT);")],
        [new MigrationReleasePin("1.0.0", 99)]
      )
    ).toThrow("Migration release pins must target an existing step or 0.");
  });
});
