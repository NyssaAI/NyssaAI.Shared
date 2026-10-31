# Responsibility: Abstract contract for reading row values with dialect type normalization.
# Public interface:
# `-- DataRecordReader
#     +-- get_boolean(col)
#     +-- get_bytes(col)
#     +-- get_date_time_offset(col)
#     +-- get_decimal(col)
#     +-- get_float(col)
#     +-- get_int(col)
#     +-- get_string(col)
#     +-- get_uuid(col)
#     +-- get_value(col)
#     `-- is_null(col)
"""Data record reader protocol."""

from datetime import datetime
from decimal import Decimal
from typing import Protocol
from uuid import UUID


class DataRecordReader(Protocol):
    """Provides index-based and column-name-based getters for typed row data."""

    def is_null(self, col: int | str) -> bool:
        """Check if column is NULL."""
        ...

    def get_boolean(self, col: int | str) -> bool:
        """Get column value as boolean."""
        ...

    def get_int(self, col: int | str) -> int:
        """Get column value as integer."""
        ...

    def get_float(self, col: int | str) -> float:
        """Get column value as float."""
        ...

    def get_decimal(self, col: int | str) -> Decimal:
        """Get column value as Decimal."""
        ...

    def get_string(self, col: int | str) -> str:
        """Get column value as string."""
        ...

    def get_uuid(self, col: int | str) -> UUID:
        """Get column value as UUID."""
        ...

    def get_date_time_offset(self, col: int | str) -> datetime:
        """Get column value as datetime."""
        ...

    def get_bytes(self, col: int | str) -> bytes:
        """Get column value as bytes."""
        ...

    def get_value(self, col: int | str) -> object:
        """Get raw column value."""
        ...
