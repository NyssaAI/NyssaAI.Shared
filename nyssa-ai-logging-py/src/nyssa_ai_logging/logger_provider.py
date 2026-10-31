# Responsibility: Factory and lifecycle protocol for creating category-scoped loggers.
# Public interface:
# `-- LoggerProvider
#     +-- create_logger(category_name)
#     `-- dispose()
"""LoggerProvider protocol for category-scoped logger creation and cleanup."""

from __future__ import annotations

from typing import Protocol

from nyssa_ai_logging.logger import Logger


class LoggerProvider(Protocol):
    """Protocol for creating loggers and disposing sinks."""

    def create_logger(self, category_name: str) -> Logger:
        """Create a category-scoped logger."""
        ...

    def dispose(self) -> None:
        """Dispose and flush underlying logging destinations."""
        ...
