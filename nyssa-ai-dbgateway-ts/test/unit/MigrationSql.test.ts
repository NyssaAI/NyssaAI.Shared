// Responsibility: Unit test transactional SQL validation logic for migrations.
// Public interface:
// (none)

import { describe, it, expect } from "vitest";
import { MigrationSql } from "../../src/migrations/MigrationSql.js";

describe("MigrationSql", () => {
  it("migrationSql_validStatements_returnsTrue", () => {
    expect(MigrationSql.isTransactional("CREATE TABLE t (id TEXT PRIMARY KEY);")).toBe(true);
    expect(
      MigrationSql.isTransactional(`
        -- initial table
        CREATE TABLE users (
          id UUID PRIMARY KEY,
          email TEXT NOT NULL
        );
        /* multi-line
           comment */
        CREATE INDEX idx_users_email ON users(email);
      `)
    ).toBe(true);
    expect(MigrationSql.isTransactional("ALTER TABLE users ADD COLUMN name TEXT;")).toBe(true);
    expect(MigrationSql.isTransactional("DROP TABLE users;")).toBe(true);
  });

  it("migrationSql_forbiddenTransactionControl_returnsFalse", () => {
    expect(MigrationSql.isTransactional("BEGIN TRANSACTION; CREATE TABLE t(id INT); COMMIT;")).toBe(false);
    expect(MigrationSql.isTransactional("START TRANSACTION;")).toBe(false);
    expect(MigrationSql.isTransactional("COMMIT;")).toBe(false);
    expect(MigrationSql.isTransactional("ROLLBACK;")).toBe(false);
    expect(MigrationSql.isTransactional("VACUUM;")).toBe(false);
    expect(MigrationSql.isTransactional("PRAGMA foreign_keys = ON;")).toBe(false);
    expect(MigrationSql.isTransactional("SET search_path = public;")).toBe(false);
  });

  it("migrationSql_unclosedCommentsOrQuotes_returnsFalse", () => {
    expect(MigrationSql.isTransactional("/* unclosed comment CREATE TABLE t (id INT);")).toBe(false);
    expect(MigrationSql.isTransactional("CREATE TABLE t (id TEXT DEFAULT 'unclosed);")).toBe(false);
    expect(MigrationSql.isTransactional("")).toBe(false);
    expect(MigrationSql.isTransactional("   ")).toBe(false);
  });
});
