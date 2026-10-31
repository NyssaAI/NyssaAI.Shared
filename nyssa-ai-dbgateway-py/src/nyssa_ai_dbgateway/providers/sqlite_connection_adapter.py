# Responsibility: SQLite connection adapter using Python standard library sqlite3.
# Public interface:
# `-- SqliteConnectionAdapter
#     +-- begin_transaction()
#     +-- close()
#     +-- commit()
#     +-- execute_non_query(sql, params, timeout_seconds)
#     +-- execute_query(sql, params, timeout_seconds)
#     +-- execute_scalar(sql, params, timeout_seconds)
#     +-- new(connection)
#     `-- rollback()
"""SQLite connection adapter."""

import sqlite3
from typing import Mapping, Sequence


class SqliteConnectionAdapter:
    """Adapter wrapping standard sqlite3.Connection."""

    def __init__(self, connection: sqlite3.Connection) -> None:
        self._conn = connection
        self._in_transaction = False

    def execute_non_query(
        self,
        sql: str,
        params: Sequence[object] | Mapping[str, object] | None = None,
        timeout_seconds: int = 30,
    ) -> int:
        cursor = self._conn.cursor()
        try:
            if params is not None:
                cursor.execute(sql, params)
            else:
                cursor.execute(sql)
            return cursor.rowcount
        finally:
            cursor.close()

    def execute_scalar(
        self,
        sql: str,
        params: Sequence[object] | Mapping[str, object] | None = None,
        timeout_seconds: int = 30,
    ) -> object:
        cursor = self._conn.cursor()
        try:
            if params is not None:
                cursor.execute(sql, params)
            else:
                cursor.execute(sql)
            row = cursor.fetchone()
            return row[0] if row is not None else None
        finally:
            cursor.close()

    def execute_query(
        self,
        sql: str,
        params: Sequence[object] | Mapping[str, object] | None = None,
        timeout_seconds: int = 30,
    ) -> tuple[list[str], list[tuple[object, ...]]]:
        cursor = self._conn.cursor()
        try:
            if params is not None:
                cursor.execute(sql, params)
            else:
                cursor.execute(sql)
            columns = [desc[0] for desc in cursor.description] if cursor.description else []
            rows = cursor.fetchall()
            return columns, rows
        finally:
            cursor.close()

    def begin_transaction(self) -> None:
        if not self._in_transaction:
            self._conn.execute("BEGIN IMMEDIATE;")
            self._in_transaction = True

    def commit(self) -> None:
        if self._in_transaction:
            self._conn.execute("COMMIT;")
            self._in_transaction = False

    def rollback(self) -> None:
        if self._in_transaction:
            self._conn.execute("ROLLBACK;")
            self._in_transaction = False

    def close(self) -> None:
        if self._in_transaction:
            try:
                self._conn.execute("ROLLBACK;")
            except Exception:
                pass
            self._in_transaction = False
        self._conn.close()
