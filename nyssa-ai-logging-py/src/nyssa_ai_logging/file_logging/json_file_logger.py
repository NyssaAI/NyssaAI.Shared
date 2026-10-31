# Responsibility: Category-scoped logger writing structured JSON log records to a LogFile.
# Public interface:
# `-- JsonFileLogger
#     +-- critical(message, error, fields, operation)
#     +-- debug(message, fields, operation)
#     +-- error(message, error, fields, operation)
#     +-- info(message, fields, operation)
#     +-- is_enabled(level)
#     +-- new(category_name, log_file, clock, minimum_level)
#     +-- trace(message, fields, operation)
#     `-- warn(message, fields, operation)
"""Category-scoped logger writing structured JSON records to a LogFile destination."""

from __future__ import annotations

from collections.abc import Mapping
import re
import traceback

from nyssa_ai_logging.log_level import LogLevel, is_log_level_enabled
from nyssa_ai_logging.file_logging.clock import Clock
from nyssa_ai_logging.file_logging.log_file import LogFile
from nyssa_ai_logging.file_logging.log_record import LogRecord
from nyssa_ai_logging.file_logging.stderr_diagnostics import StderrDiagnostics

_PLACEHOLDER_RE = re.compile(r"\{(\w+)\}")


class JsonFileLogger:
    """Category-scoped logger that formats records and appends them to a LogFile."""

    def __init__(
        self,
        category_name: str,
        log_file: LogFile,
        clock: Clock,
        minimum_level: LogLevel,
    ) -> None:
        """Initialize with category name, LogFile sink, clock, and minimum level."""
        self._category_name = category_name
        self._log_file = log_file
        self._clock = clock
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
        """Log a trace record."""
        self._write("Trace", message, fields, operation, None)

    def debug(
        self,
        message: str,
        fields: Mapping[str, object] | None = None,
        operation: str = "",
    ) -> None:
        """Log a debug record."""
        self._write("Debug", message, fields, operation, None)

    def info(
        self,
        message: str,
        fields: Mapping[str, object] | None = None,
        operation: str = "",
    ) -> None:
        """Log an information record."""
        self._write("Information", message, fields, operation, None)

    def warn(
        self,
        message: str,
        fields: Mapping[str, object] | None = None,
        operation: str = "",
    ) -> None:
        """Log a warning record."""
        self._write("Warning", message, fields, operation, None)

    def error(
        self,
        message: str,
        error: BaseException | None = None,
        fields: Mapping[str, object] | None = None,
        operation: str = "",
    ) -> None:
        """Log an error record."""
        self._write("Error", message, fields, operation, error)

    def critical(
        self,
        message: str,
        error: BaseException | None = None,
        fields: Mapping[str, object] | None = None,
        operation: str = "",
    ) -> None:
        """Log a critical record."""
        self._write("Critical", message, fields, operation, error)

    def _write(
        self,
        level: LogLevel,
        message: str,
        fields: Mapping[str, object] | None,
        operation: str,
        error: BaseException | None,
    ) -> None:
        if not self.is_enabled(level):
            return

        try:
            formatted_message = self._format_template(message, fields)
            exception_str = self._format_exception(error)

            record = LogRecord(
                Timestamp=self._clock.now().isoformat(),
                Level=level,
                Message=formatted_message,
                Module=self._category_name,
                Operation=operation,
                Exception=exception_str,
                AdditionalFields=dict(fields) if fields is not None else {},
            )
            self._log_file.write_record(record)
        except Exception as ex:
            StderrDiagnostics.error(self._category_name, f"failed to log to file: {ex}")

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

    def _format_exception(self, error: BaseException | None) -> str | None:
        if error is None:
            return None
        return "".join(traceback.format_exception(type(error), error, error.__traceback__)).strip()
