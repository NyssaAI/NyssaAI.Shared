# Responsibility: Create stdlib logging.Handler instances that write formatted log lines to sys.stderr.
# Public interface:
# `-- create_stderr_handler(minimum_level)
"""Stdlib logging.Handler routing operational records to standard error with sink isolation."""

from __future__ import annotations

import logging
import os
import sys

from nyssa_ai_logging.file_logging.extract_nyssa_record import extract_nyssa_record
from nyssa_ai_logging.log_level import LogLevel, to_python_log_level

_LEVEL_TAGS: dict[str, str] = {
    "Trace": "trce",
    "Debug": "dbug",
    "Information": "info",
    "Warning": "warn",
    "Error": "fail",
    "Critical": "crit",
    "None": "none",
}


def create_stderr_handler(minimum_level: LogLevel | int | None = None) -> logging.Handler:
    """Create a stdlib logging.Handler that writes to sys.stderr."""
    py_level = to_python_log_level(minimum_level) if minimum_level is not None else logging.NOTSET

    class _StderrHandler(logging.Handler):
        """Stdlib logging.Handler writing records to sys.stderr with sink isolation."""

        def __init__(self) -> None:
            super().__init__(level=py_level)

        def emit(self, record: logging.LogRecord) -> None:
            try:
                nyssa_record = extract_nyssa_record(record)
                tag = _LEVEL_TAGS.get(nyssa_record.Level, "info")
                output = (
                    f"{tag}: {nyssa_record.Module}{os.linesep}"
                    f"      {nyssa_record.Message}{os.linesep}"
                )
                if nyssa_record.Exception is not None:
                    output += f"      {nyssa_record.Exception}{os.linesep}"
                sys.stderr.write(output)
                sys.stderr.flush()
            except BaseException:
                pass

    return _StderrHandler()
