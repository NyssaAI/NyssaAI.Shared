"""Unit tests for LogRecordSerializer."""

from __future__ import annotations

from datetime import UTC, datetime
import json

from nyssa_ai_logging.file_logging.log_record import LogRecord
from nyssa_ai_logging.file_logging.log_record_serializer import LogRecordSerializer


def test_serializer_valid_record_emits_expected_json_structure() -> None:
    record = LogRecord(
        Timestamp="2026-10-13T12:00:00.000Z",
        Level="Information",
        Message="Order 1042 placed",
        Module="OrderService",
        Operation="OrderPlaced",
        Exception=None,
        AdditionalFields={"OrderId": 1042, "Customer": "Alice", "Active": True},
    )

    serialized = LogRecordSerializer.serialize(record)
    parsed = json.loads(serialized)

    assert parsed["Timestamp"] == "2026-10-13T12:00:00.000Z"
    assert parsed["Level"] == "Information"
    assert parsed["Message"] == "Order 1042 placed"
    assert parsed["Module"] == "OrderService"
    assert parsed["Operation"] == "OrderPlaced"
    assert parsed["Exception"] is None

    fields = parsed["AdditionalFields"]
    assert fields["OrderId"] == 1042
    assert fields["Customer"] == "Alice"
    assert fields["Active"] is True


def test_serializer_various_field_types_preserves_types_safely() -> None:
    now = datetime(2026, 10, 13, 12, 0, 0, tzinfo=UTC)
    err = RuntimeError("database unavailable")

    record = LogRecord(
        Timestamp=now.isoformat(),
        Level="Error",
        Message="Failed to connect",
        Module="DatabaseClient",
        Operation="Connect",
        Exception=str(err),
        AdditionalFields={
            "timeout_ms": 5000,
            "attempt_date": now,
            "inner_error": err,
            "metadata": {"host": "localhost", "port": 5432},
        },
    )

    serialized = LogRecordSerializer.serialize(record)
    parsed = json.loads(serialized)
    fields = parsed["AdditionalFields"]

    assert fields["timeout_ms"] == 5000
    assert fields["attempt_date"] == now.isoformat()
    assert fields["inner_error"] == "database unavailable"
    assert fields["metadata"]["host"] == "localhost"
