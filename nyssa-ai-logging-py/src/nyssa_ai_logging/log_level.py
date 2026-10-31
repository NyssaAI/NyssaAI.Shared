# Responsibility: Define log level values and level severity comparison logic.
# Public interface:
# +-- LogLevel
# +-- is_log_level_enabled(level, minimum_level)
# `-- parse_log_level(value)
"""Log level definitions and comparison logic."""

from __future__ import annotations

from typing import Literal

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


def parse_log_level(value: str | None) -> LogLevel:
    """Parse a string value into a recognized LogLevel."""
    if not value:
        return "Information"
    normalized = value.strip().lower()
    match normalized:
        case "trace":
            return "Trace"
        case "debug":
            return "Debug"
        case "information" | "info":
            return "Information"
        case "warning" | "warn":
            return "Warning"
        case "error":
            return "Error"
        case "critical" | "crit":
            return "Critical"
        case "none":
            return "None"
        case _:
            return "Information"


def is_log_level_enabled(level: LogLevel, minimum_level: LogLevel) -> bool:
    """Return True if level meets or exceeds the minimum_level severity."""
    if level == "None":
        return False
    level_severity = _LEVEL_SEVERITY.get(level, 2)
    minimum_severity = _LEVEL_SEVERITY.get(minimum_level, 2)
    return level_severity >= minimum_severity
