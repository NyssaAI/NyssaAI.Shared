# Responsibility: Output formatted operational log lines to standard output with sink isolation.
# Public interface:
# `-- ConsoleOutputLogger
#     +-- critical(message, error, fields, operation)
#     +-- debug(message, fields, operation)
#     +-- error(message, error, fields, operation)
#     +-- info(message, fields, operation)
#     +-- is_enabled(level)
#     +-- new(category_name, minimum_level)
#     +-- trace(message, fields, operation)
#     `-- warn(message, fields, operation)
"""Category-scoped logger directing output to standard output."""

from __future__ import annotations

from collections.abc import Mapping
import os
import re
import sys
import traceback

from nyssa_ai_logging.log_level import LogLevel, is_log_level_enabled

_LEVEL_TAGS: dict[LogLevel, str] = {
    "Trace": "trce",
    "Debug": "dbug",
    "Information": "info",
    "Warning": "warn",
    "Error": "fail",
    "Critical": "crit",
    "None": "none",
}

_PLACEHOLDER_RE = re.compile(r"\{(\w+)\}")


class ConsoleOutputLogger:
    """Logger that formats and writes records to sys.stdout with sink isolation."""

    def __init__(self, category_name: str, minimum_level: LogLevel) -> None:
        """Initialize with category name and minimum level."""
        self._category_name = category_name
        self._minimum_level = minimum_level

    def is_enabled(self, level: LogLevel) -> bool:
        """Return True when the supplied level meets or exceeds the minimum level."""
        return is_log_level_enabled(level, self._minimum_level)

    def trace(
        self,
        message: str,
        fields: Mapping[str, object] | None = None,
        operation: str = "",
    ) -> None:
        """Log a trace record to standard output."""
        self._write("Trace", message, fields, operation, None)

    def debug(
        self,
        message: str,
        fields: Mapping[str, object] | None = None,
        operation: str = "",
    ) -> None:
        """Log a debug record to standard output."""
        self._write("Debug", message, fields, operation, None)

    def info(
        self,
        message: str,
        fields: Mapping[str, object] | None = None,
        operation: str = "",
    ) -> None:
        """Log an information record to standard output."""
        self._write("Information", message, fields, operation, None)

    def warn(
        self,
        message: str,
        fields: Mapping[str, object] | None = None,
        operation: str = "",
    ) -> None:
        """Log a warning record to standard output."""
        self._write("Warning", message, fields, operation, None)

    def error(
        self,
        message: str,
        error: BaseException | None = None,
        fields: Mapping[str, object] | None = None,
        operation: str = "",
    ) -> None:
        """Log an error record to standard output."""
        self._write("Error", message, fields, operation, error)

    def critical(
        self,
        message: str,
        error: BaseException | None = None,
        fields: Mapping[str, object] | None = None,
        operation: str = "",
    ) -> None:
        """Log a critical record to standard output."""
        self._write("Critical", message, fields, operation, error)

    def _write(
        self,
        level: LogLevel,
        message: str,
        fields: Mapping[str, object] | None,
        _operation: str,
        error: BaseException | None,
    ) -> None:
        if not self.is_enabled(level):
            return

        try:
            tag = _LEVEL_TAGS.get(level, "info")
            formatted = self._format_template(message, fields)
            output = f"{tag}: {self._category_name}{os.linesep}      {formatted}{os.linesep}"

            if error is not None:
                exc_text = "".join(
                    traceback.format_exception(type(error), error, error.__traceback__)
                ).strip()
                output += f"      {exc_text}{os.linesep}"

            sys.stdout.write(output)
            sys.stdout.flush()
        except BaseException:
            # PY-LOG: Sink failures are contained and do not fail the business operation.
            pass

    def _format_template(
        self,
        template: str,
        fields: Mapping[str, object] | None,
    ) -> str:
        if not fields:
            return template

        def replacer(match: re.Match[str]) -> str:
            key = match.group(1)
            if key in fields:
                return str(fields[key])
            return match.group(0)

        return _PLACEHOLDER_RE.sub(replacer, template)
