# Responsibility: Factory function configuring default Nyssa logging with standard error and optional JSON file.
# Public interface:
# `-- create_nyssa_logging(file_path, minimum_level, clock)
"""Factory function for single-call Nyssa logging setup."""

from __future__ import annotations

from nyssa_ai_logging.log_level import LogLevel
from nyssa_ai_logging.file_logging.clock import Clock
from nyssa_ai_logging.configuration.logging_builder import LoggingBuilder


def create_nyssa_logging(
    file_path: str | None = None,
    minimum_level: LogLevel = "Information",
    clock: Clock | None = None,
) -> LoggingBuilder:
    """Configure default Nyssa logging to standard error and optional JSON file."""
    builder = LoggingBuilder(minimum_level, clock)
    builder.with_stderr(minimum_level)
    if file_path:
        builder.add_nyssa_json_file(file_path, minimum_level)
    return builder
