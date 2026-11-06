# Responsibility: Define log level values, stdlib logging level mappings, and severity comparison logic.
# Public interface:
# +-- LogLevel
# +-- TRACE_LEVEL_NUM
# +-- from_python_log_level(level_no)
# +-- is_log_level_enabled(level, minimum_level)
# +-- parse_log_level(value)
# `-- to_python_log_level(level)
"""Log level definitions and Python stdlib logging conversions."""

from __future__ import annotations

import logging
from typing import Final, Literal

TRACE_LEVEL_NUM: Final[int] = 5
logging.addLevelName(TRACE_LEVEL_NUM, "TRACE")

LogLevel = Literal[
    "Trace",
    "Debug",
    "Information",
    "Warning",
    "Error",
    "Critical",
    "None",
]

_LEVEL_SEVERITY: dict[LogLevel, int] = {
    "Trace": 0,
    "Debug": 1,
    "Information": 2,
    "Warning": 3,
    "Error": 4,
    "Critical": 5,
    "None": 6,
}

_TO_PYTHON_LEVEL: dict[LogLevel, int] = {
    "Trace": TRACE_LEVEL_NUM,
    "Debug": logging.DEBUG,
    "Information": logging.INFO,
    "Warning": logging.WARNING,
    "Error": logging.ERROR,
    "Critical": logging.CRITICAL,
    "None": logging.CRITICAL + 50,
}


def parse_log_level(value: str | int | None) -> LogLevel:
    """Parse a string or stdlib logging integer into a recognized LogLevel."""
    if isinstance(value, int):
        return from_python_log_level(value)
    if not value:
        return "Information"
    normalized = value.strip().lower()
    match normalized:
        case "trace" | "notset":
            return "Trace"
        case "debug":
            return "Debug"
        case "information" | "info":
            return "Information"
        case "warning" | "warn":
            return "Warning"
        case "error":
            return "Error"
        case "critical" | "crit" | "fatal":
            return "Critical"
        case "none":
            return "None"
        case _:
            return "Information"


def to_python_log_level(level: LogLevel | int | str) -> int:
    """Convert a Nyssa LogLevel or stdlib integer level into a Python logging integer level."""
    if isinstance(level, int):
        return level
    parsed = parse_log_level(level)
    return _TO_PYTHON_LEVEL.get(parsed, logging.INFO)


def from_python_log_level(level_no: int) -> LogLevel:
    """Map a Python stdlib numeric log level to a canonical Nyssa LogLevel."""
    if level_no >= logging.CRITICAL + 50:
        return "None"
    if level_no >= logging.CRITICAL:
        return "Critical"
    if level_no >= logging.ERROR:
        return "Error"
    if level_no >= logging.WARNING:
        return "Warning"
    if level_no >= logging.INFO:
        return "Information"
    if level_no >= logging.DEBUG:
        return "Debug"
    return "Trace"


def is_log_level_enabled(level: LogLevel, minimum_level: LogLevel) -> bool:
    """Return True if level meets or exceeds the minimum_level severity."""
    if level == "None" or minimum_level == "None":
        return False
    level_severity = _LEVEL_SEVERITY.get(level, 2)
    minimum_severity = _LEVEL_SEVERITY.get(minimum_level, 2)
    return level_severity >= minimum_severity
