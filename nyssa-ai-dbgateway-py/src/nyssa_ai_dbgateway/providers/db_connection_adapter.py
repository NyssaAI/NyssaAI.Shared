# Responsibility: Define low-level connection abstraction protocol.
# Public interface:
# `-- DbConnectionAdapter
#     +-- begin_transaction()
#     +-- close()
#     +-- commit()
#     +-- execute_non_query(sql, params, timeout_seconds)
#     +-- execute_query(sql, params, timeout_seconds)
#     +-- execute_scalar(sql, params, timeout_seconds)
#     `-- rollback()
"""Connection adapter protocol."""

from typing import Protocol, Sequence, Mapping


class DbConnectionAdapter(Protocol):
    """Abstract connection wrapper for raw query execution and transactions."""

    def execute_non_query(
        self,
        sql: str,
        params: Sequence[object] | Mapping[str, object] | None = None,
        timeout_seconds: int = 30,
    ) -> int:
        """Execute command and return rows affected."""
        ...

    def execute_scalar(
        self,
        sql: str,
        params: Sequence[object] | Mapping[str, object] | None = None,
        timeout_seconds: int = 30,
    ) -> object:
        """Execute query and return scalar first column value."""
        ...

    def execute_query(
        self,
        sql: str,
        params: Sequence[object] | Mapping[str, object] | None = None,
        timeout_seconds: int = 30,
    ) -> tuple[list[str], list[tuple[object, ...]]]:
        """Execute query and return column names and rows."""
        ...

    def begin_transaction(self) -> None:
        """Begin transaction on current connection."""
        ...

    def commit(self) -> None:
        """Commit transaction."""
        ...

    def rollback(self) -> None:
        """Rollback transaction."""
        ...

    def close(self) -> None:
        """Close connection."""
        ...
