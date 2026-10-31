# Responsibility: Protocol declaring active transaction contract.
# Public interface:
# `-- DbTransaction
#     +-- commit()
#     +-- database_provider
#     +-- dialect
#     +-- execute(command_or_sql, params)
#     +-- execute_command(command)
#     +-- query(query_or_sql, params)
#     +-- query_single(query_or_sql, params)
#     +-- rollback()
#     `-- scalar(sql, params)
"""DbTransaction protocol."""

from types import TracebackType
from typing import Mapping, Protocol, Sequence, TypeVar
from nyssa_ai_dbgateway.types import DbDialect
from nyssa_ai_dbgateway.commands.db_command import DbCommand
from nyssa_ai_dbgateway.commands.db_non_query import DbNonQuery
from nyssa_ai_dbgateway.commands.db_query import DbQuery

TResult = TypeVar("TResult")
TRow = TypeVar("TRow")


class DbTransaction(Protocol):
    """Scoped transaction implementing execution with commit, rollback, and context management."""

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

    def commit(self) -> None:
        """Commit transaction changes."""
        ...

    def rollback(self) -> None:
        """Rollback transaction changes."""
        ...

    def __enter__(self) -> "DbTransaction":
        """Enter transaction context."""
        ...

    def __exit__(
        self,
        exc_type: type[BaseException] | None,
        exc_val: BaseException | None,
        exc_tb: TracebackType | None,
    ) -> None:
        """Exit transaction context, committing if no exception, rolling back otherwise."""
        ...
