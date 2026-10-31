# Responsibility: SQLite database provider implementation.
# Public interface:
# `-- DbProviderSqlite
#     +-- database_provider
#     +-- dialect
#     +-- new(connection_string)
#     +-- open_connection()
#     `-- prepare(settings)
"""SQLite database provider."""

import os
import sqlite3
from nyssa_ai_dbgateway.types import DbDialect, DbGatewayConfiguration
from nyssa_ai_dbgateway.providers.db_connection_adapter import DbConnectionAdapter
from nyssa_ai_dbgateway.providers.sqlite_connection_adapter import SqliteConnectionAdapter


class DbProviderSqlite:
    """Database provider for SQLite using stdlib sqlite3."""

    def __init__(self, connection_string: str) -> None:
        self._connection_string = connection_string
        self._path = self._parse_file_path(connection_string)

    @property
    def database_provider(self) -> str:
        return "sqlite"

    @property
    def dialect(self) -> DbDialect:
        return "sqlite"

    def open_connection(self) -> DbConnectionAdapter:
        if self._path != ":memory:" and self._path:
            dir_path = os.path.dirname(self._path)
            if dir_path:
                os.makedirs(dir_path, exist_ok=True)

        conn = sqlite3.connect(self._path, timeout=30.0, autocommit=True, isolation_level=None)
        conn.execute("PRAGMA foreign_keys = ON;")
        if self._path != ":memory:":
            conn.execute("PRAGMA journal_mode = WAL;")
        return SqliteConnectionAdapter(conn)

    @staticmethod
    def prepare(settings: DbGatewayConfiguration) -> "DbProviderSqlite":
        return DbProviderSqlite(settings.connection_string)

    @staticmethod
    def _parse_file_path(connection_string: str) -> str:
        for part in connection_string.split(";"):
            trimmed = part.strip()
            if trimmed.lower().startswith("data source="):
                return trimmed[12:].strip()
            if trimmed.lower().startswith("uri=file:"):
                return trimmed[9:].strip()
        return connection_string.strip()
