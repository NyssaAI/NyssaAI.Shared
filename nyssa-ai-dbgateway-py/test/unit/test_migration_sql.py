# Responsibility: Unit test transactional SQL validation logic for migrations.
# Public interface:
# +-- test_migration_sql_forbidden_transaction_control_returns_false()
# +-- test_migration_sql_unclosed_comments_or_quotes_returns_false()
# `-- test_migration_sql_valid_statements_returns_true()
"""Unit tests for MigrationSql transactional SQL validation."""

from nyssa_ai_dbgateway.migrations.migration_sql import MigrationSql


def test_migration_sql_valid_statements_returns_true() -> None:
    assert MigrationSql.is_transactional("CREATE TABLE t (id TEXT PRIMARY KEY);") is True
    assert (
        MigrationSql.is_transactional(
            """
            -- initial table
            CREATE TABLE users (
              id UUID PRIMARY KEY,
              email TEXT NOT NULL
            );
            /* multi-line
               comment */
            CREATE INDEX idx_users_email ON users(email);
            """
        )
        is True
    )
    assert MigrationSql.is_transactional("ALTER TABLE users ADD COLUMN name TEXT;") is True
    assert MigrationSql.is_transactional("DROP TABLE users;") is True
    assert (
        MigrationSql.is_transactional(
            "CREATE TRIGGER trg_users AFTER INSERT ON users BEGIN UPDATE users SET email = email; END;"
        )
        is True
    )


def test_migration_sql_forbidden_transaction_control_returns_false() -> None:
    assert MigrationSql.is_transactional("BEGIN TRANSACTION; CREATE TABLE t(id INT); COMMIT;") is False
    assert MigrationSql.is_transactional("START TRANSACTION;") is False
    assert MigrationSql.is_transactional("COMMIT;") is False
    assert MigrationSql.is_transactional("ROLLBACK;") is False
    assert MigrationSql.is_transactional("VACUUM;") is False
    assert MigrationSql.is_transactional("PRAGMA foreign_keys = ON;") is False
    assert MigrationSql.is_transactional("SET search_path = public;") is False


def test_migration_sql_unclosed_comments_or_quotes_returns_false() -> None:
    assert MigrationSql.is_transactional("/* unclosed comment CREATE TABLE t (id INT);") is False
    assert MigrationSql.is_transactional("CREATE TABLE t (id TEXT DEFAULT 'unclosed);") is False
    assert MigrationSql.is_transactional("") is False
    assert MigrationSql.is_transactional("   ") is False
    assert MigrationSql.is_transactional(None) is False
