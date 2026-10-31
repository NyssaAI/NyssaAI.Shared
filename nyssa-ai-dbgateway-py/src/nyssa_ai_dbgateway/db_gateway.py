# Responsibility: Primary database gateway contract.
# Public interface:
# `-- DbGateway
#     +-- begin_transaction()
#     +-- database_provider
#     +-- dialect
#     +-- execute(command_or_sql, params)
#     +-- execute_command(command)
#     +-- execute_transaction(operation)
#     +-- query(query_or_sql, params)
#     +-- query_single(query_or_sql, params)
#     `-- scalar(sql, params)
"""DbGateway protocol."""

from typing import Callable, Mapping, Protocol, Sequence, TypeVar
from nyssa_ai_dbgateway.types import DbDialect
from nyssa_ai_dbgateway.commands.db_command import DbCommand
from nyssa_ai_dbgateway.commands.db_non_query import DbNonQuery
from nyssa_ai_dbgateway.commands.db_query import DbQuery
from nyssa_ai_dbgateway.db_transaction import DbTransaction

T = TypeVar("T")
TResult = TypeVar("TResult")
TRow = TypeVar("TRow")


class DbGateway(Protocol):
    """Primary gateway contract with execution and transaction boundaries."""

    @property
    def database_provider(self) -> str:
        """Provider name."""
        ...

    @property
    def dialect(self) -> DbDialect:
        """Dialect identifier."""
        ...

    def execute(
        self,
        command_or_sql: DbCommand[object] | DbNonQuery | str,
        params: Mapping[str, object] | Sequence[object] | None = None,
    ) -> object:
        """Execute command or ad-hoc SQL returning rows affected or mapped outcome."""
        ...

    def execute_command(self, command: DbCommand[TResult]) -> TResult:
        """Execute a typed command."""
        ...

    def scalar(
        self,
        sql: str,
        params: Mapping[str, object] | Sequence[object] | None = None,
    ) -> object:
        """Execute query returning scalar value."""
        ...

    def query(
        self,
        query_or_sql: DbQuery[object] | str,
        params: Mapping[str, object] | Sequence[object] | None = None,
    ) -> list[object]:
        """Query multiple rows."""
        ...

    def query_single(
        self,
        query_or_sql: DbQuery[object] | str,
        params: Mapping[str, object] | Sequence[object] | None = None,
    ) -> object:
        """Query a single row or return None."""
        ...

    def begin_transaction(self) -> DbTransaction:
        """Begin an active transaction."""
        ...

    def execute_transaction(self, operation: Callable[[DbTransaction], T]) -> T:
        """Execute a unit of work inside an automatic transaction scope."""
        ...
