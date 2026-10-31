# Responsibility: PostgreSQL connection adapter using psycopg.
# Public interface:
# `-- PostgresConnectionAdapter
#     +-- begin_transaction()
#     +-- close()
#     +-- commit()
#     +-- execute_non_query(sql, params, timeout_seconds)
#     +-- execute_query(sql, params, timeout_seconds)
#     +-- execute_scalar(sql, params, timeout_seconds)
#     +-- new(connection)
#     `-- rollback()
"""PostgreSQL connection adapter."""

from typing import Mapping, Sequence
import psycopg


class PostgresConnectionAdapter:
    """Adapter wrapping psycopg.Connection."""

    def __init__(self, connection: object) -> None:
        if not isinstance(connection, psycopg.Connection):
            raise TypeError("connection must be a psycopg.Connection")
        self._conn = connection

    def execute_non_query(
        self,
        sql: str,
        params: Sequence[object] | Mapping[str, object] | None = None,
        timeout_seconds: int = 30,
    ) -> int:
        with self._conn.cursor() as cursor:
            cursor.execute(sql, params)
            return int(cursor.rowcount)

    def execute_scalar(
        self,
        sql: str,
        params: Sequence[object] | Mapping[str, object] | None = None,
        timeout_seconds: int = 30,
    ) -> object:
        with self._conn.cursor() as cursor:
            cursor.execute(sql, params)
            row = cursor.fetchone()
            return row[0] if row is not None else None

    def execute_query(
        self,
        sql: str,
        params: Sequence[object] | Mapping[str, object] | None = None,
        timeout_seconds: int = 30,
    ) -> tuple[list[str], list[tuple[object, ...]]]:
        with self._conn.cursor() as cursor:
            cursor.execute(sql, params)
            columns = [desc[0] for desc in cursor.description] if cursor.description else []
            rows = cursor.fetchall()
            return columns, rows

    def begin_transaction(self) -> None:
        pass

    def commit(self) -> None:
        self._conn.commit()

    def rollback(self) -> None:
        self._conn.rollback()

    def close(self) -> None:
        self._conn.close()
