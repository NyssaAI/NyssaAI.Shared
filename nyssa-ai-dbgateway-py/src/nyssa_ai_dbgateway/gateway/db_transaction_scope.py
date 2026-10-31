# Responsibility: Scoped transaction implementation coordinating connection lifecycle.
# Public interface:
# `-- DbTransactionScope
#     +-- commit()
#     +-- database_provider
#     +-- dialect
#     +-- execute(command_or_sql, params)
#     +-- execute_command(command)
#     +-- new(conn, provider_name, dialect, command_timeout_seconds, logger)
#     +-- query(query_or_sql, params)
#     +-- query_single(query_or_sql, params)
#     +-- rollback()
#     `-- scalar(sql, params)
"""Scoped transaction implementation."""

import time
from types import TracebackType
from typing import Mapping, Sequence, TypeVar
from nyssa_ai_dbgateway.types import DbDialect
from nyssa_ai_dbgateway.commands.db_command import DbCommand
from nyssa_ai_dbgateway.commands.db_non_query import DbNonQuery
from nyssa_ai_dbgateway.commands.db_query import DbQuery
from nyssa_ai_dbgateway.providers.db_connection_adapter import DbConnectionAdapter
from nyssa_ai_dbgateway.gateway.db_command_executor import DbCommandExecutor
from nyssa_ai_dbgateway.logging.db_gateway_logger import DbGatewayLogger

TResult = TypeVar("TResult")
TRow = TypeVar("TRow")


class DbTransactionScope:
    """Active transaction scope wrapping a dedicated connection."""

    def __init__(
        self,
        conn: DbConnectionAdapter,
        provider_name: str,
        dialect: DbDialect,
        command_timeout_seconds: int = 30,
        logger: DbGatewayLogger | None = None,
    ) -> None:
        self._conn = conn
        self._provider_name = provider_name
        self._dialect = dialect
        self._timeout = command_timeout_seconds
        self._logger = logger
        self._completed = False

    @property
    def database_provider(self) -> str:
        return self._provider_name

    @property
    def dialect(self) -> DbDialect:
        return self._dialect

    def execute(
        self,
        command_or_sql: DbCommand[object] | DbNonQuery | str,
        params: Mapping[str, object] | Sequence[object] | None = None,
    ) -> object:
        started = time.monotonic()
        try:
            if isinstance(command_or_sql, str):
                res: object = DbCommandExecutor.execute_ad_hoc(self._conn, self._dialect, self._timeout, command_or_sql, params)
            elif isinstance(command_or_sql, DbCommand):
                res = DbCommandExecutor.execute_command(self._conn, self._dialect, self._timeout, command_or_sql)
            else:
                res = DbCommandExecutor.execute_non_query(self._conn, self._dialect, self._timeout, command_or_sql)
            if self._logger:
                self._logger.debug("tx.execute", {"elapsed_ms": (time.monotonic() - started) * 1000, "outcome": "success"})
            return res
        except Exception as err:
            if self._logger:
                self._logger.error("tx.execute", {"elapsed_ms": (time.monotonic() - started) * 1000, "outcome": "failure"}, err)
            raise

    def execute_command(self, command: DbCommand[TResult]) -> TResult:
        res = self.execute(command)
        return res  # type: ignore[return-value]

    def scalar(
        self,
        sql: str,
        params: Mapping[str, object] | Sequence[object] | None = None,
    ) -> object:
        started = time.monotonic()
        try:
            res = DbCommandExecutor.execute_scalar(self._conn, self._dialect, self._timeout, sql, params)
            if self._logger:
                self._logger.debug("tx.scalar", {"elapsed_ms": (time.monotonic() - started) * 1000, "outcome": "success"})
            return res
        except Exception as err:
            if self._logger:
                self._logger.error("tx.scalar", {"elapsed_ms": (time.monotonic() - started) * 1000, "outcome": "failure"}, err)
            raise

    def query(
        self,
        query_or_sql: DbQuery[object] | str,
        params: Mapping[str, object] | Sequence[object] | None = None,
    ) -> list[object]:
        started = time.monotonic()
        try:
            if isinstance(query_or_sql, str):
                rows: list[object] = list(DbCommandExecutor.execute_ad_hoc_query(self._conn, self._dialect, self._timeout, query_or_sql, params))
            else:
                rows = list(DbCommandExecutor.execute_query(self._conn, self._dialect, self._timeout, query_or_sql))
            if self._logger:
                self._logger.debug("tx.query", {"elapsed_ms": (time.monotonic() - started) * 1000, "outcome": "success", "count": len(rows)})
            return rows
        except Exception as err:
            if self._logger:
                self._logger.error("tx.query", {"elapsed_ms": (time.monotonic() - started) * 1000, "outcome": "failure"}, err)
            raise

    def query_single(
        self,
        query_or_sql: DbQuery[object] | str,
        params: Mapping[str, object] | Sequence[object] | None = None,
    ) -> object:
        rows = self.query(query_or_sql, params)
        return rows[0] if rows else None

    def commit(self) -> None:
        if not self._completed:
            self._conn.commit()
            self._completed = True

    def rollback(self) -> None:
        if not self._completed:
            self._conn.rollback()
            self._completed = True

    def __enter__(self) -> "DbTransactionScope":
        return self

    def __exit__(
        self,
        exc_type: type[BaseException] | None,
        exc_val: BaseException | None,
        exc_tb: TracebackType | None,
    ) -> None:
        try:
            if exc_type is not None:
                self.rollback()
            else:
                self.commit()
        finally:
            self._conn.close()
