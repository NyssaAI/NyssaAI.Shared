# Responsibility: Abstract contract for opening database connections.
# Public interface:
# `-- DbProvider
#     +-- database_provider
#     +-- dialect
#     `-- open_connection()
"""Database provider protocol."""

from typing import Protocol
from nyssa_ai_dbgateway.types import DbDialect
from nyssa_ai_dbgateway.providers.db_connection_adapter import DbConnectionAdapter


class DbProvider(Protocol):
    """Abstract provider that opens database connections."""

    @property
    def database_provider(self) -> str:
        """Provider name."""
        ...

    @property
    def dialect(self) -> DbDialect:
        """Dialect identifier."""
        ...

    def open_connection(self) -> DbConnectionAdapter:
        """Open a new connection."""
        ...
