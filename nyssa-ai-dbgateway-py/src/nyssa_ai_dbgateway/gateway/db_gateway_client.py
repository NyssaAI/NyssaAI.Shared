# Responsibility: Primary database gateway client implementing DbGateway.
# Public interface:
# `-- DbGatewayClient
#     +-- begin_transaction()
#     +-- create(settings, custom_registrations, logger)
#     +-- database_provider
#     +-- dialect
#     +-- execute(command_or_sql, params)
#     +-- execute_command(command)
#     +-- execute_transaction(operation)
#     +-- new(provider, command_timeout_seconds, logger)
#     +-- query(query_or_sql, params)
#     +-- query_single(query_or_sql, params)
#     `-- scalar(sql, params)
"""DbGatewayClient coordinating providers, ad-hoc execution, typed commands, and transaction scopes."""

import time
from typing import Callable, Mapping, Sequence, TypeVar
from nyssa_ai_dbgateway.types import DbDialect, DbGatewayConfiguration
from nyssa_ai_dbgateway.db_transaction import DbTransaction
from nyssa_ai_dbgateway.commands.db_command import DbCommand
from nyssa_ai_dbgateway.commands.db_non_query import DbNonQuery
from nyssa_ai_dbgateway.commands.db_query import DbQuery
from nyssa_ai_dbgateway.providers.db_provider import DbProvider
from nyssa_ai_dbgateway.providers.db_provider_registration import DbProviderRegistration
from nyssa_ai_dbgateway.providers.db_provider_registry import DbProviderRegistry
from nyssa_ai_dbgateway.gateway.db_command_executor import DbCommandExecutor
from nyssa_ai_dbgateway.gateway.db_transaction_scope import DbTransactionScope
from nyssa_ai_dbgateway.logging.db_gateway_logger import DbGatewayLogger

T = TypeVar("T")
TResult = TypeVar("TResult")
TRow = TypeVar("TRow")


class DbGatewayClient:
    """Primary zero-ceremony database gateway."""

    def __init__(
        self,
        provider: DbProvider,
        command_timeout_seconds: int = 30,
        logger: DbGatewayLogger | None = None,
    ) -> None:
        self._provider = provider
        self._timeout = max(0, command_timeout_seconds)
        self._logger = logger

    @property
    def database_provider(self) -> str:
        return self._provider.database_provider

    @property
    def dialect(self) -> DbDialect:
        return self._provider.dialect

    @staticmethod
    def create(
        settings: DbGatewayConfiguration,
        custom_registrations: Sequence[DbProviderRegistration] | None = None,
        logger: DbGatewayLogger | None = None,
    ) -> "DbGatewayClient":
        factory = DbProviderRegistry.register_database_providers(settings, custom_registrations)
        provider = factory()
        return DbGatewayClient(provider, settings.command_timeout_seconds, logger)

    def execute(
        self,
        command_or_sql: DbCommand[object] | DbNonQuery | str,
        params: Mapping[str, object] | Sequence[object] | None = None,
    ) -> object:
        started = time.monotonic()
        conn = self._provider.open_connection()
        try:
            if isinstance(command_or_sql, str):
                res: object = DbCommandExecutor.execute_ad_hoc(conn, self.dialect, self._timeout, command_or_sql, params)
            elif isinstance(command_or_sql, DbCommand):
                res = DbCommandExecutor.execute_command(conn, self.dialect, self._timeout, command_or_sql)
            else:
                res = DbCommandExecutor.execute_non_query(conn, self.dialect, self._timeout, command_or_sql)
            if self._logger:
                self._logger.debug("gateway.execute", {"elapsed_ms": (time.monotonic() - started) * 1000, "outcome": "success"})
            return res
        except Exception as err:
            if self._logger:
                self._logger.error("gateway.execute", {"elapsed_ms": (time.monotonic() - started) * 1000, "outcome": "failure"}, err)
            raise
        finally:
            conn.close()

    def execute_command(self, command: DbCommand[TResult]) -> TResult:
        res = self.execute(command)
        return res  # type: ignore[return-value]

    def scalar(
        self,
        sql: str,
        params: Mapping[str, object] | Sequence[object] | None = None,
    ) -> object:
        started = time.monotonic()
        conn = self._provider.open_connection()
        try:
            res = DbCommandExecutor.execute_scalar(conn, self.dialect, self._timeout, sql, params)
            if self._logger:
                self._logger.debug("gateway.scalar", {"elapsed_ms": (time.monotonic() - started) * 1000, "outcome": "success"})
            return res
        except Exception as err:
            if self._logger:
                self._logger.error("gateway.scalar", {"elapsed_ms": (time.monotonic() - started) * 1000, "outcome": "failure"}, err)
            raise
        finally:
            conn.close()

    def query(
        self,
        query_or_sql: DbQuery[object] | str,
        params: Mapping[str, object] | Sequence[object] | None = None,
    ) -> list[object]:
        started = time.monotonic()
        conn = self._provider.open_connection()
        try:
            if isinstance(query_or_sql, str):
                rows: list[object] = list(DbCommandExecutor.execute_ad_hoc_query(conn, self.dialect, self._timeout, query_or_sql, params))
            else:
                rows = list(DbCommandExecutor.execute_query(conn, self.dialect, self._timeout, query_or_sql))
            if self._logger:
                self._logger.debug("gateway.query", {"elapsed_ms": (time.monotonic() - started) * 1000, "outcome": "success", "count": len(rows)})
            return rows
        except Exception as err:
            if self._logger:
                self._logger.error("gateway.query", {"elapsed_ms": (time.monotonic() - started) * 1000, "outcome": "failure"}, err)
            raise
        finally:
            conn.close()

    def query_single(
        self,
        query_or_sql: DbQuery[object] | str,
        params: Mapping[str, object] | Sequence[object] | None = None,
    ) -> object:
        rows = self.query(query_or_sql, params)
        return rows[0] if rows else None

    def begin_transaction(self) -> DbTransaction:
        conn = self._provider.open_connection()
        conn.begin_transaction()
        return DbTransactionScope(
            conn,
            self.database_provider,
            self.dialect,
            self._timeout,
            self._logger,
        )

    def execute_transaction(self, operation: Callable[[DbTransaction], T]) -> T:
        with self.begin_transaction() as tx:
            return operation(tx)
