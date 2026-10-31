# Responsibility: Concrete parameter binder implementation for SQLite and PostgreSQL.
# Public interface:
# `-- DbParameterBinder
#     +-- add(name, value)
#     +-- add_many(parameters)
#     +-- new()
#     +-- to_postgres_values(param_names)
#     `-- to_sqlite_dict()
"""Concrete parameter binder."""

from datetime import date, datetime
from decimal import Decimal
from typing import Mapping
from uuid import UUID


class DbParameterBinder:
    """Holds parameters and transforms them per dialect."""

    def __init__(self) -> None:
        self._values: dict[str, object] = {}

    def add(self, name: str, value: object) -> None:
        """Add parameter, stripping leading '@' or ':' if present."""
        clean_name = name.lstrip("@:")
        self._values[clean_name] = value

    def add_many(self, parameters: Mapping[str, object]) -> None:
        """Add multiple parameters."""
        for k, v in parameters.items():
            self.add(k, v)

    def to_sqlite_dict(self) -> dict[str, object]:
        """Convert values to SQLite-compatible primitives (integers, strings, floats, bytes, None)."""
        result: dict[str, object] = {}
        for k, v in self._values.items():
            result[k] = self._coerce_sqlite_value(v)
        return result

    def to_postgres_values(self, param_names: list[str]) -> list[object]:
        """Map parameter names to ordered positional values for PostgreSQL."""
        result: list[object] = []
        for name in param_names:
            clean_name = name.lstrip("@:")
            if clean_name not in self._values:
                raise ValueError(f"Missing parameter value for '{clean_name}'.")
            result.append(self._values[clean_name])
        return result

    @staticmethod
    def _coerce_sqlite_value(val: object) -> object:
        if val is None:
            return None
        if isinstance(val, bool):
            return 1 if val else 0
        if isinstance(val, (int, float, str, bytes)):
            return val
        if isinstance(val, Decimal):
            return str(val)
        if isinstance(val, (datetime, date)):
            return val.isoformat()
        if isinstance(val, UUID):
            return str(val)
        if isinstance(val, bytearray):
            return bytes(val)
        return str(val)
