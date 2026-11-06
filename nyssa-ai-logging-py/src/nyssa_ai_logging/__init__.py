# Responsibility: Export the public surface of nyssa_ai_logging.
# Public interface:
# (none)
"""NyssaAI Logging for Python stdlib logging."""

from nyssa_ai_logging.configuration.configure_nyssa_logging import configure_nyssa_logging
from nyssa_ai_logging.configuration.logging_builder import LoggingBuilder, resolve_app_name
from nyssa_ai_logging.console_logging.create_console_handler import create_console_handler
from nyssa_ai_logging.console_logging.create_stderr_handler import create_stderr_handler
from nyssa_ai_logging.file_logging.clock import Clock
from nyssa_ai_logging.file_logging.create_json_file_handler import create_json_file_handler
from nyssa_ai_logging.file_logging.extract_nyssa_record import extract_nyssa_record
from nyssa_ai_logging.file_logging.log_file import LogFile
from nyssa_ai_logging.file_logging.log_record import LogRecord
from nyssa_ai_logging.file_logging.log_record_serializer import LogRecordSerializer
from nyssa_ai_logging.file_logging.owner_only_access import OwnerOnlyAccess
from nyssa_ai_logging.file_logging.stderr_diagnostics import StderrDiagnostics
from nyssa_ai_logging.file_logging.system_clock import SystemClock
from nyssa_ai_logging.log_level import (
    TRACE_LEVEL_NUM,
    LogLevel,
    from_python_log_level,
    is_log_level_enabled,
    parse_log_level,
    to_python_log_level,
)
from nyssa_ai_logging.telemetry.create_otel_handler import create_otel_handler
from nyssa_ai_logging.telemetry.span_scope import SpanScope
from nyssa_ai_logging.telemetry.start_span import (
    get_active_span,
    is_otel_enabled,
    set_otel_enabled,
    start_span,
)

__all__ = [
    "TRACE_LEVEL_NUM",
    "Clock",
    "LogFile",
    "LogLevel",
    "LogRecord",
    "LogRecordSerializer",
    "LoggingBuilder",
    "OwnerOnlyAccess",
    "SpanScope",
    "StderrDiagnostics",
    "SystemClock",
    "configure_nyssa_logging",
    "create_console_handler",
    "create_json_file_handler",
    "create_otel_handler",
    "create_stderr_handler",
    "extract_nyssa_record",
    "from_python_log_level",
    "get_active_span",
    "is_log_level_enabled",
    "is_otel_enabled",
    "parse_log_level",
    "resolve_app_name",
    "set_otel_enabled",
    "start_span",
    "to_python_log_level",
]
