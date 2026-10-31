# Responsibility: Serialize LogRecord instances to single UTF-8 JSON lines conforming to Nyssa schema.
# Public interface:
# `-- LogRecordSerializer
#     `-- serialize(record)
"""Serialization of LogRecord to JSON strings."""

from __future__ import annotations

from datetime import date, datetime
import json
import math
from typing import Final

from nyssa_ai_logging.file_logging.log_record import LogRecord


def _sanitize_field_value(value: object) -> object:
    """Sanitize individual additional field values for JSON compliance."""
    if value is None:
        return None
    if isinstance(value, (str, bool)):
        return value
    if isinstance(value, int):
        return value
    if isinstance(value, float):
        return value if math.isfinite(value) else str(value)
    if isinstance(value, (datetime, date)):
        return value.isoformat()
    if isinstance(value, BaseException):
        return str(value)
    if isinstance(value, (dict, list, tuple)):
        try:
            # Test JSON round-trip
            return json.loads(json.dumps(value, default=str))
        except (TypeError, ValueError):
            return str(value)
    return str(value)


class LogRecordSerializer:
    """Static serializer for converting LogRecord into single-line JSON string."""

    @staticmethod
    def serialize(record: LogRecord) -> str:
        """Serialize a LogRecord into a single JSON line without newlines."""
        sanitized_fields: dict[str, object] = {
            k: _sanitize_field_value(v) for k, v in record.AdditionalFields.items()
        }

        payload: dict[str, object] = {
            "Timestamp": record.Timestamp,
            "Level": record.Level,
            "Message": record.Message,
            "Module": record.Module,
            "Operation": record.Operation,
            "Exception": record.Exception,
            "AdditionalFields": sanitized_fields,
        }

        return json.dumps(payload, separators=(",", ":"), ensure_ascii=False)
