# Responsibility: Factory and lifecycle manager for rotating JSON file logger instances.
# Public interface:
# `-- JsonFileLoggerProvider
#     +-- create_logger(category_name)
#     +-- dispose()
#     +-- new(log_file, clock, minimum_level)
#     `-- try_create(file_path, clock, minimum_level)
"""Provider creating and managing JsonFileLogger instances."""

from __future__ import annotations

from nyssa_ai_logging.log_level import LogLevel
from nyssa_ai_logging.logger import Logger
from nyssa_ai_logging.file_logging.clock import Clock
from nyssa_ai_logging.file_logging.system_clock import SystemClock
from nyssa_ai_logging.file_logging.json_file_logger import JsonFileLogger
from nyssa_ai_logging.file_logging.log_file import LogFile
from nyssa_ai_logging.file_logging.stderr_diagnostics import StderrDiagnostics


class JsonFileLoggerProvider:
    """Creates JsonFileLogger instances and manages the lifetime of a LogFile."""

    def __init__(
        self,
        log_file: LogFile,
        clock: Clock,
        minimum_level: LogLevel,
    ) -> None:
        """Initialize with existing LogFile, clock, and minimum level."""
        self._log_file = log_file
        self._clock = clock
        self._minimum_level = minimum_level
        self._disposed = False

    @staticmethod
    def try_create(
        file_path: str,
        clock: Clock | None = None,
        minimum_level: LogLevel = "Trace",
    ) -> JsonFileLoggerProvider | None:
        """Attempt to open and initialize a LogFile destination, returning None on failure."""
        if not file_path or not file_path.strip():
            return None

        effective_clock = clock if clock is not None else SystemClock()
        log_file = LogFile(file_path, None, effective_clock)
        error = log_file.initialize()
        if error is not None:
            log_file.dispose()
            StderrDiagnostics.error(file_path, error)
            return None

        return JsonFileLoggerProvider(log_file, effective_clock, minimum_level)

    def create_logger(self, category_name: str) -> Logger:
        """Create a JsonFileLogger for category_name."""
        return JsonFileLogger(
            category_name, self._log_file, self._clock, self._minimum_level
        )

    def dispose(self) -> None:
        """Close and dispose the owned LogFile."""
        if self._disposed:
            return
        self._disposed = True
        self._log_file.dispose()
