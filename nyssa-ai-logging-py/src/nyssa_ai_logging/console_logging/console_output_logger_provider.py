# Responsibility: Provider for category-scoped standard output loggers.
# Public interface:
# `-- ConsoleOutputLoggerProvider
#     +-- create_logger(category_name)
#     +-- dispose()
#     `-- new(minimum_level)
"""Provider creating ConsoleOutputLogger instances."""

from __future__ import annotations

from nyssa_ai_logging.log_level import LogLevel
from nyssa_ai_logging.logger import Logger
from nyssa_ai_logging.console_logging.console_output_logger import ConsoleOutputLogger


class ConsoleOutputLoggerProvider:
    """Creates ConsoleOutputLogger instances with a configured minimum level."""

    def __init__(self, minimum_level: LogLevel = "Information") -> None:
        """Initialize with minimum logging level."""
        self._minimum_level = minimum_level

    def create_logger(self, category_name: str) -> Logger:
        """Create a ConsoleOutputLogger for category_name."""
        return ConsoleOutputLogger(category_name, self._minimum_level)

    def dispose(self) -> None:
        """Dispose the provider; standard output requires no resource teardown."""
        pass
