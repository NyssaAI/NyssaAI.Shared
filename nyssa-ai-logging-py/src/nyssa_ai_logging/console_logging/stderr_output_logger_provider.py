# Responsibility: Provider for category-scoped standard error loggers.
# Public interface:
# `-- StderrOutputLoggerProvider
#     +-- create_logger(category_name)
#     +-- dispose()
#     `-- new(minimum_level)
"""Provider creating StderrOutputLogger instances."""

from __future__ import annotations

from nyssa_ai_logging.log_level import LogLevel
from nyssa_ai_logging.logger import Logger
from nyssa_ai_logging.console_logging.stderr_output_logger import StderrOutputLogger


class StderrOutputLoggerProvider:
    """Creates StderrOutputLogger instances with a configured minimum level."""

    def __init__(self, minimum_level: LogLevel = "Information") -> None:
        """Initialize with minimum logging level."""
        self._minimum_level = minimum_level

    def create_logger(self, category_name: str) -> Logger:
        """Create a StderrOutputLogger for category_name."""
        return StderrOutputLogger(category_name, self._minimum_level)

    def dispose(self) -> None:
        """Dispose the provider; standard error requires no resource teardown."""
        pass
