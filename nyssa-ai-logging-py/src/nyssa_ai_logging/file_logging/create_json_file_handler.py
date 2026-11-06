# Responsibility: Create stdlib logging.Handler instances that write rotating Nyssa JSONL records.
# Public interface:
# `-- create_json_file_handler(file_path, minimum_level, clock)
"""Factory and stdlib logging.Handler implementation for rotating JSON log files."""

from __future__ import annotations

import logging

from nyssa_ai_logging.file_logging.clock import Clock
from nyssa_ai_logging.file_logging.extract_nyssa_record import extract_nyssa_record
from nyssa_ai_logging.file_logging.log_file import LogFile
from nyssa_ai_logging.file_logging.stderr_diagnostics import StderrDiagnostics
from nyssa_ai_logging.file_logging.system_clock import SystemClock
from nyssa_ai_logging.log_level import LogLevel, to_python_log_level


def create_json_file_handler(
    file_path: str,
    minimum_level: LogLevel | int | None = None,
    clock: Clock | None = None,
) -> logging.Handler | None:
    """Open and initialize a rotating JSON file logging.Handler, returning None on failure."""
    if not file_path or not file_path.strip():
        StderrDiagnostics.error(file_path or "<empty>", "Invalid log path: empty path")
        return None

    effective_clock = clock if clock is not None else SystemClock()
    log_file = LogFile(file_path, None, effective_clock)
    error = log_file.initialize()
    if error is not None:
        log_file.dispose()
        StderrDiagnostics.error(file_path, error)
        return None

    py_level = to_python_log_level(minimum_level) if minimum_level is not None else logging.NOTSET

    class _JsonFileHandler(logging.Handler):
        """Stdlib logging.Handler forwarding records to a rotating LogFile."""

        def __init__(self) -> None:
            super().__init__(level=py_level)
            self._closed = False

        def emit(self, record: logging.LogRecord) -> None:
            if self._closed:
                return
            try:
                nyssa_record = extract_nyssa_record(record, effective_clock)
                log_file.write_record(nyssa_record)
            except Exception as ex:
                StderrDiagnostics.error(record.name, f"failed to log to file: {ex}")

        def close(self) -> None:
            if not self._closed:
                self._closed = True
                log_file.dispose()
            super().close()

    return _JsonFileHandler()
