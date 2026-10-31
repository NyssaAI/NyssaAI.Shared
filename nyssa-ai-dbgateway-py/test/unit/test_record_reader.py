# Responsibility: Unit test record reader types and conversion normalization.
# Public interface:
# `-- test_db_record_reader_normalized_getters()
"""Unit tests for DbRecordReader."""

from decimal import Decimal
from uuid import UUID
from nyssa_ai_dbgateway.commands.db_record_reader import DbRecordReader


def test_db_record_reader_normalized_getters() -> None:
    row = [1, "42", "3.1415", "12345678-1234-5678-1234-567812345678", "2026-10-31T12:00:00+00:00", b"hello", None]
    cols = ["is_active", "age", "pi", "guid", "created", "blob", "empty"]
    reader = DbRecordReader(row, cols)

    assert reader.get_boolean("is_active") is True
    assert reader.get_boolean(0) is True
    assert reader.get_int("age") == 42
    assert reader.get_int(1) == 42
    assert reader.get_decimal("pi") == Decimal("3.1415")
    assert reader.get_uuid("guid") == UUID("12345678-1234-5678-1234-567812345678")
    assert reader.get_bytes("blob") == b"hello"
    assert reader.is_null("empty") is True
    assert reader.is_null("is_active") is False
