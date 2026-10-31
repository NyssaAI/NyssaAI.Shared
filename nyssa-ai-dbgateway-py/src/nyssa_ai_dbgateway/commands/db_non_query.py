# Responsibility: Define typed command protocol for modifying database state.
# Public interface:
# `-- DbNonQuery
#     +-- bind_parameters(parameters, dialect)
#     `-- get_sql(dialect)
"""Typed non-query command protocol."""

from typing import Protocol
from nyssa_ai_dbgateway.types import DbDialect
from nyssa_ai_dbgateway.commands.parameter_binder import ParameterBinder


class DbNonQuery(Protocol):
    """Represents a database command that returns rows affected."""

    def get_sql(self, dialect: DbDialect) -> str:
        """Return the SQL statement for the target dialect."""
        ...

    def bind_parameters(self, parameters: ParameterBinder, dialect: DbDialect) -> None:
        """Bind parameter values."""
        ...
