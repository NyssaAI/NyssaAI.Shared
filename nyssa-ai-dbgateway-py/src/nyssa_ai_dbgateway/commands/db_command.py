# Responsibility: Define typed command protocol returning custom mapped outcomes.
# Public interface:
# `-- DbCommand
#     +-- bind_parameters(parameters, dialect)
#     +-- get_sql(dialect)
#     `-- map_result(reader)
"""Typed command protocol returning custom scalar or aggregate outcomes."""

from typing import Protocol, TypeVar, runtime_checkable
from nyssa_ai_dbgateway.types import DbDialect
from nyssa_ai_dbgateway.commands.parameter_binder import ParameterBinder
from nyssa_ai_dbgateway.commands.data_record_reader import DataRecordReader

TResult_co = TypeVar("TResult_co", covariant=True)


@runtime_checkable
class DbCommand(Protocol[TResult_co]):
    """Represents a database command that maps its complete result set."""

    def get_sql(self, dialect: DbDialect) -> str:
        """Return the SQL statement for the target dialect."""
        ...

    def bind_parameters(self, parameters: ParameterBinder, dialect: DbDialect) -> None:
        """Bind command parameters."""
        ...

    def map_result(self, reader: DataRecordReader) -> TResult_co:
        """Map reader state into target result."""
        ...
