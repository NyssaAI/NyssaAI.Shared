# Responsibility: Bridge Python stdlib logging records into OpenTelemetry logs with trace correlation.
# Public interface:
# `-- create_otel_handler(minimum_level, clock, record_sink)
"""Stdlib logging.Handler forwarding records to the OpenTelemetry Logs API."""

from __future__ import annotations

from collections.abc import Callable
import logging

from opentelemetry import _logs as otel_logs
from opentelemetry._logs import SeverityNumber
from opentelemetry.trace import TraceFlags

from nyssa_ai_logging.file_logging.clock import Clock
from nyssa_ai_logging.file_logging.extract_nyssa_record import extract_nyssa_record
from nyssa_ai_logging.file_logging.log_record import LogRecord
from nyssa_ai_logging.file_logging.system_clock import SystemClock
from nyssa_ai_logging.log_level import LogLevel, to_python_log_level

_SEVERITY_MAP: dict[str, SeverityNumber] = {
    "Trace": SeverityNumber.TRACE,
    "Debug": SeverityNumber.DEBUG,
    "Information": SeverityNumber.INFO,
    "Warning": SeverityNumber.WARN,
    "Error": SeverityNumber.ERROR,
    "Critical": SeverityNumber.FATAL,
}


def _to_otel_primitive(value: object) -> str | bool | int | float:
    """Coerce an attribute value to an OpenTelemetry-compatible scalar."""
    if isinstance(value, (str, bool, int, float)):
        return value
    return str(value)


def create_otel_handler(
    minimum_level: LogLevel | int | None = None,
    clock: Clock | None = None,
    record_sink: Callable[[LogRecord], None] | None = None,
) -> logging.Handler:
    """Create a stdlib logging.Handler that bridges records to OpenTelemetry."""
    effective_clock = clock if clock is not None else SystemClock()
    py_level = to_python_log_level(minimum_level) if minimum_level is not None else logging.NOTSET

    class _OtelLoggingHandler(logging.Handler):
        """Stdlib logging.Handler emitting OpenTelemetry LogRecords."""

        def __init__(self) -> None:
            super().__init__(level=py_level)

        def emit(self, record: logging.LogRecord) -> None:
            try:
                nyssa_record = extract_nyssa_record(record, effective_clock)
                if record_sink is not None:
                    record_sink(nyssa_record)

                trace_id_hex = str(nyssa_record.AdditionalFields.get("TraceId", ""))
                span_id_hex = str(nyssa_record.AdditionalFields.get("SpanId", ""))
                trace_id_int = int(trace_id_hex, 16) if len(trace_id_hex) == 32 else 0
                span_id_int = int(span_id_hex, 16) if len(span_id_hex) == 16 else 0

                attrs: dict[str, str | bool | int | float] = {
                    "module": nyssa_record.Module,
                }
                if nyssa_record.Operation:
                    attrs["operation"] = nyssa_record.Operation
                for k, v in nyssa_record.AdditionalFields.items():
                    if v is not None:
                        attrs[k] = _to_otel_primitive(v)

                now_ns = int(effective_clock.now().timestamp() * 1_000_000_000)
                severity = _SEVERITY_MAP.get(nyssa_record.Level, SeverityNumber.INFO)

                otel_logger = otel_logs.get_logger(nyssa_record.Module or "nyssa_ai_logging")
                trace_flags = (
                    TraceFlags(TraceFlags.SAMPLED)
                    if trace_id_int
                    else TraceFlags(TraceFlags.DEFAULT)
                )
                otel_record = otel_logs.LogRecord(
                    timestamp=now_ns,
                    observed_timestamp=now_ns,
                    trace_id=trace_id_int,
                    span_id=span_id_int,
                    trace_flags=trace_flags,
                    severity_text=nyssa_record.Level,
                    severity_number=severity,
                    body=nyssa_record.Message,
                    attributes=attrs,
                )
                otel_logger.emit(otel_record)
            except BaseException:
                pass

    return _OtelLoggingHandler()
