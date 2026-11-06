# Responsibility: Convert Python stdlib logging.LogRecord instances into canonical Nyssa LogRecord structures.
# Public interface:
# `-- extract_nyssa_record(record, clock)
"""Conversion and structured field extraction from stdlib logging.LogRecord."""

from __future__ import annotations

from collections.abc import Mapping
from enum import Enum
import logging
import re
import traceback
from typing import Final

from nyssa_ai_logging.file_logging.clock import Clock
from nyssa_ai_logging.file_logging.log_record import LogRecord
from nyssa_ai_logging.file_logging.system_clock import SystemClock
from nyssa_ai_logging.log_level import from_python_log_level
from nyssa_ai_logging.telemetry.start_span import get_active_span

_PLACEHOLDER_RE: Final[re.Pattern[str]] = re.compile(r"\{(\w+)\}")
_BASE_LOG_RECORD_FACTORY = logging.getLogRecordFactory()


def _install_record_factory() -> None:
    """Install a LogRecord factory whose getMessage() supports both % and {Placeholder} templates."""

    class _NyssaStdlibLogRecord(logging.LogRecord):
        def getMessage(self) -> str:
            raw_msg = str(self.msg)
            placeholder_names = _PLACEHOLDER_RE.findall(raw_msg)
            if placeholder_names:
                fields: dict[str, object] = {}
                for key, val in self.__dict__.items():
                    if not key.startswith("_"):
                        fields[key] = val
                if isinstance(self.args, Mapping):
                    for k, v in self.args.items():
                        fields[str(k)] = v
                    return _format_template(raw_msg, fields)
                if isinstance(self.args, tuple) and len(self.args) > 0:
                    if len(self.args) == 1 and isinstance(self.args[0], Mapping):
                        for k, v in self.args[0].items():
                            fields[str(k)] = v
                    else:
                        for name, arg_val in zip(placeholder_names, self.args):
                            fields[name] = arg_val
                    return _format_template(raw_msg, fields)
                return _format_template(raw_msg, fields)
            try:
                return super().getMessage()
            except TypeError:
                return raw_msg

    def _nyssa_record_factory(*args: object, **kwargs: object) -> logging.LogRecord:
        record = _BASE_LOG_RECORD_FACTORY(*args, **kwargs)
        record.__class__ = _NyssaStdlibLogRecord
        return record

    logging.setLogRecordFactory(_nyssa_record_factory)


_install_record_factory()

_STANDARD_LOG_RECORD_KEYS: Final[frozenset[str]] = frozenset({
    "args",
    "asctime",
    "created",
    "exc_info",
    "exc_text",
    "filename",
    "funcName",
    "levelname",
    "levelno",
    "lineno",
    "message",
    "module",
    "msecs",
    "msg",
    "name",
    "pathname",
    "process",
    "processName",
    "relativeCreated",
    "stack_info",
    "taskName",
    "thread",
    "threadName",
    "operation",
    "Operation",
    "fields",
    "error",
})


def _display_value(value: object) -> str:
    """Format a placeholder value for human-readable message substitution."""
    if isinstance(value, Enum):
        return value.name
    return str(value)


def _format_template(template: str, fields: Mapping[str, object]) -> str:
    """Substitute {Placeholder} tokens in template with matching values from fields."""
    if not fields:
        return template

    def replacer(match: re.Match[str]) -> str:
        key = match.group(1)
        if key in fields:
            return _display_value(fields[key])
        return match.group(0)

    return _PLACEHOLDER_RE.sub(replacer, template)


def _extract_exception(record: logging.LogRecord) -> str | None:
    """Extract formatted traceback from record.exc_info or extra['error']."""
    if record.exc_info and record.exc_info[0] is not None and record.exc_info[1] is not None:
        exc_type, exc_val, exc_tb = record.exc_info
        return "".join(traceback.format_exception(exc_type, exc_val, exc_tb)).strip()

    extra_error = record.__dict__.get("error")
    if isinstance(extra_error, BaseException):
        return "".join(
            traceback.format_exception(type(extra_error), extra_error, extra_error.__traceback__)
        ).strip()

    if record.exc_text:
        return str(record.exc_text).strip()

    return None


def extract_nyssa_record(
    record: logging.LogRecord,
    clock: Clock | None = None,
) -> LogRecord:
    """Convert a Python logging.LogRecord into a structured Nyssa LogRecord."""
    effective_clock = clock if clock is not None else SystemClock()
    fields: dict[str, object] = {}

    active_span = get_active_span()
    span_operation = ""
    if active_span is not None and active_span.is_recording:
        span_operation = active_span.operation
        for k, v in active_span.attributes.items():
            fields[k] = v
        if active_span.trace_id:
            fields["TraceId"] = active_span.trace_id
        if active_span.span_id:
            fields["SpanId"] = active_span.span_id
        if active_span.parent_span_id:
            fields["ParentSpanId"] = active_span.parent_span_id

    extra_fields = record.__dict__.get("fields")
    if isinstance(extra_fields, Mapping):
        for k, v in extra_fields.items():
            fields[str(k)] = v

    for key, val in record.__dict__.items():
        if key not in _STANDARD_LOG_RECORD_KEYS and not key.startswith("_"):
            fields[key] = val

    raw_msg = str(record.msg)
    placeholder_names = _PLACEHOLDER_RE.findall(raw_msg)
    raw_args = record.args

    if isinstance(raw_args, Mapping):
        for k, v in raw_args.items():
            fields[str(k)] = v
        formatted_message = _format_template(raw_msg, fields)
    elif placeholder_names and isinstance(raw_args, tuple) and len(raw_args) > 0:
        if len(raw_args) == 1 and isinstance(raw_args[0], Mapping):
            for k, v in raw_args[0].items():
                fields[str(k)] = v
        else:
            for name, arg_val in zip(placeholder_names, raw_args):
                fields[name] = arg_val
        formatted_message = _format_template(raw_msg, fields)
    elif placeholder_names and not raw_args:
        formatted_message = _format_template(raw_msg, fields)
    else:
        try:
            formatted_message = record.getMessage()
        except Exception:
            formatted_message = _format_template(raw_msg, fields)

    raw_op = record.__dict__.get("operation") or record.__dict__.get("Operation")
    operation = str(raw_op) if isinstance(raw_op, str) and raw_op else span_operation

    return LogRecord(
        Timestamp=effective_clock.now().isoformat(),
        Level=from_python_log_level(record.levelno),
        Message=formatted_message,
        Module=record.name,
        Operation=operation,
        Exception=_extract_exception(record),
        AdditionalFields=fields,
    )
