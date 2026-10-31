# Responsibility: Declare migration history SQL statements and advisory locking commands.
# Public interface:
# +-- MIGRATIONS_TABLE_NAME
# +-- SQL_LOCK_POSTGRES
# +-- SQL_SELECT_HISTORY
# +-- SQL_TABLE_POSTGRES
# +-- SQL_TABLE_SQLITE
# `-- SQL_UNLOCK_POSTGRES
"""Migration SQL commands and constants."""

MIGRATIONS_TABLE_NAME = "nyssa_dbgateway_migrations"

SQL_TABLE_SQLITE = f"""
CREATE TABLE IF NOT EXISTS {MIGRATIONS_TABLE_NAME} (
    number INTEGER PRIMARY KEY,
    fingerprint TEXT NOT NULL
);
"""

SQL_TABLE_POSTGRES = f"""
CREATE TABLE IF NOT EXISTS {MIGRATIONS_TABLE_NAME} (
    number BIGINT PRIMARY KEY,
    fingerprint TEXT NOT NULL
);
"""

SQL_SELECT_HISTORY = f"""
SELECT number, fingerprint FROM {MIGRATIONS_TABLE_NAME} ORDER BY number ASC;
"""

SQL_LOCK_POSTGRES = "SELECT pg_advisory_lock(564535375002);"
SQL_UNLOCK_POSTGRES = "SELECT pg_advisory_unlock(564535375002);"
