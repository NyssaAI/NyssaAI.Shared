# Responsibility: Define typed query protocol mapping individual rows into target instances.
# Public interface:
# `-- DbQuery
#     +-- bind_parameters(parameters, dialect)
#     +-- get_sql(dialect)
#     `-- map_row(reader)
"""Typed query protocol."""

from typing import Protocol, TypeVar
from nyssa_ai_dbgateway.types import DbDialect
from nyssa_ai_dbgateway.commands.parameter_binder import ParameterBinder
from nyssa_ai_dbgateway.commands.data_record_reader import DataRecordReader

TRow_co = TypeVar("TRow_co", covariant=True)


class DbQuery(Protocol[TRow_co]):
    """Represents a database query that yields rows mapped by map_row."""

    def get_sql(self, dialect: DbDialect) -> str:
        """Return the SQL query for the target dialect."""
        ...

    def bind_parameters(self, parameters: ParameterBinder, dialect: DbDialect) -> None:
        """Bind query parameters."""
        ...

    def map_row(self, reader: DataRecordReader) -> TRow_co:
        """Map the current row into a domain model or DTO."""
        ...
