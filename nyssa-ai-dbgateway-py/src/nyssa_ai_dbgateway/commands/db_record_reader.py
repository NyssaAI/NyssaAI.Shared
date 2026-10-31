# Responsibility: Concrete reader implementation normalizing SQLite and PostgreSQL row values.
# Public interface:
# `-- DbRecordReader
#     +-- get_boolean(col)
#     +-- get_bytes(col)
#     +-- get_date_time_offset(col)
#     +-- get_decimal(col)
#     +-- get_float(col)
#     +-- get_int(col)
#     +-- get_string(col)
#     +-- get_uuid(col)
#     +-- get_value(col)
#     +-- is_null(col)
#     `-- new(row, columns, dialect)
"""Concrete record reader normalizing dialect variances."""

from datetime import datetime
from decimal import Decimal
from typing import Mapping, Sequence
from uuid import UUID
from nyssa_ai_dbgateway.types import DbDialect


class DbRecordReader:
    """Normalizes row values from SQLite and PostgreSQL."""

    def __init__(
        self,
        row: Sequence[object] | Mapping[str, object],
        columns: Sequence[str] | None = None,
        dialect: DbDialect = "sqlite",
    ) -> None:
        self._dialect = dialect
        if isinstance(row, Mapping):
            self._dict = {k.lower(): v for k, v in row.items()}
            self._list = list(row.values())
            self._col_map = {k.lower(): idx for idx, k in enumerate(row.keys())}
        else:
            self._list = list(row)
            self._col_map = {name.lower(): idx for idx, name in enumerate(columns or [])}
            self._dict = {name.lower(): self._list[idx] for name, idx in self._col_map.items()}

    def _get_raw(self, col: int | str) -> object:
        if isinstance(col, int):
            if col < 0 or col >= len(self._list):
                raise IndexError(f"Column index {col} out of range.")
            return self._list[col]
        name = col.lower()
        if name in self._dict:
            return self._dict[name]
        idx = self._col_map.get(name)
        if idx is not None:
            return self._list[idx]
        raise KeyError(f"Column '{col}' not found.")

    def is_null(self, col: int | str) -> bool:
        return self._get_raw(col) is None

    def get_boolean(self, col: int | str) -> bool:
        val = self._get_raw(col)
        if val is None:
            raise ValueError(f"Column {col} is null.")
        if isinstance(val, bool):
            return val
        if isinstance(val, int):
            return val != 0
        if isinstance(val, str):
            return val.lower() in ("true", "1", "t", "yes", "y")
        return bool(val)

    def get_int(self, col: int | str) -> int:
        val = self._get_raw(col)
        if val is None:
            raise ValueError(f"Column {col} is null.")
        if isinstance(val, (int, float, str, bytes, bytearray)):
            return int(val)
        raise ValueError(f"Cannot convert value {val} to int.")

    def get_float(self, col: int | str) -> float:
        val = self._get_raw(col)
        if val is None:
            raise ValueError(f"Column {col} is null.")
        if isinstance(val, (int, float, str, bytes, bytearray)):
            return float(val)
        raise ValueError(f"Cannot convert value {val} to float.")

    def get_decimal(self, col: int | str) -> Decimal:
        val = self._get_raw(col)
        if val is None:
            raise ValueError(f"Column {col} is null.")
        if isinstance(val, Decimal):
            return val
        return Decimal(str(val))

    def get_string(self, col: int | str) -> str:
        val = self._get_raw(col)
        if val is None:
            raise ValueError(f"Column {col} is null.")
        return str(val)

    def get_uuid(self, col: int | str) -> UUID:
        val = self._get_raw(col)
        if val is None:
            raise ValueError(f"Column {col} is null.")
        if isinstance(val, UUID):
            return val
        if isinstance(val, str):
            return UUID(val)
        if isinstance(val, bytes):
            return UUID(bytes=val)
        raise ValueError(f"Cannot convert value {val} to UUID.")

    def get_date_time_offset(self, col: int | str) -> datetime:
        val = self._get_raw(col)
        if val is None:
            raise ValueError(f"Column {col} is null.")
        if isinstance(val, datetime):
            return val
        if isinstance(val, str):
            clean = val.replace("Z", "+00:00")
            return datetime.fromisoformat(clean)
        raise ValueError(f"Cannot convert value {val} to datetime.")

    def get_bytes(self, col: int | str) -> bytes:
        val = self._get_raw(col)
        if val is None:
            raise ValueError(f"Column {col} is null.")
        if isinstance(val, bytes):
            return val
        if isinstance(val, (bytearray, memoryview)):
            return bytes(val)
        raise ValueError(f"Cannot convert value {val} to bytes.")

    def get_value(self, col: int | str) -> object:
        return self._get_raw(col)
