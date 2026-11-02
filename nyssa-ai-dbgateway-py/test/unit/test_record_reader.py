# Responsibility: Unit test record reader types and conversion normalization.
# Public interface:
# +-- test_db_record_reader_case_insensitive_names_and_ordinals()
# +-- test_db_record_reader_normalized_getters()
# `-- test_db_record_reader_type_coercions()
"""Unit tests for DbRecordReader."""

from datetime import datetime, timezone
from decimal import Decimal
from uuid import UUID
import pytest
from nyssa_ai_dbgateway.commands.db_record_reader import DbRecordReader


def test_db_record_reader_normalized_getters() -> None:
    row = [
        1,
        "42",
        "3.1415",
        "12345678-1234-5678-1234-567812345678",
        "2026-10-31T12:00:00+00:00",
        b"hello",
        None,
    ]
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


def test_db_record_reader_type_coercions() -> None:
    raw_uuid = UUID("12345678-1234-1234-1234-123456789abc")
    row_dict = {
        "boolIntTrue": 1,
        "boolIntFalse": 0,
        "boolStrTrue": "true",
        "boolNative": True,
        "guidStr": "12345678-1234-1234-1234-123456789abc",
        "guidBytes": raw_uuid.bytes,
        "int64Val": 9007199254740993,
        "doubleVal": 3.14159,
        "decimalVal": "12345678901234567890.123456789",
        "dateIsoZ": "2026-10-31T12:00:00Z",
        "dateObj": datetime(2026, 10, 31, 12, 0, 0, tzinfo=timezone.utc),
        "bytesArray": bytearray([1, 2, 3]),
        "nullVal": None,
    }
    reader = DbRecordReader(row_dict)

    assert reader.get_boolean("boolIntTrue") is True
    assert reader.get_boolean("boolIntFalse") is False
    assert reader.get_boolean("boolStrTrue") is True
    assert reader.get_boolean("boolNative") is True

    assert reader.get_uuid("guidStr") == raw_uuid
    assert reader.get_uuid("guidBytes") == raw_uuid

    assert reader.get_int("int64Val") == 9007199254740993
    assert reader.get_float("doubleVal") == pytest.approx(3.14159)
    assert reader.get_decimal("decimalVal") == Decimal("12345678901234567890.123456789")

    assert reader.get_date_time_offset("dateIsoZ") == datetime(2026, 10, 31, 12, 0, 0, tzinfo=timezone.utc)
    assert reader.get_date_time_offset("dateObj") == datetime(2026, 10, 31, 12, 0, 0, tzinfo=timezone.utc)

    assert reader.get_bytes("bytesArray") == b"\x01\x02\x03"
    assert reader.is_null("nullVal") is True
    with pytest.raises(ValueError, match="is null"):
        reader.get_string("nullVal")


def test_db_record_reader_case_insensitive_names_and_ordinals() -> None:
    reader = DbRecordReader({"FirstName": "Alice", "AGE": 30})
    assert reader.get_string(0) == "Alice"
    assert reader.get_string("firstname") == "Alice"
    assert reader.get_string("FIRSTNAME") == "Alice"
    assert reader.get_int(1) == 30
    assert reader.get_int("age") == 30
    assert reader.get_int("Age") == 30
    with pytest.raises(KeyError):
        reader.get_string("unknown")
    with pytest.raises(IndexError):
        reader.get_string(99)
