# Responsibility: Dispatch structured log messages to multiple underlying logger instances.
# Public interface:
# `-- CompositeLogger
#     +-- critical(message, error, fields, operation)
#     +-- debug(message, fields, operation)
#     +-- error(message, error, fields, operation)
#     +-- info(message, fields, operation)
#     +-- is_enabled(level)
#     +-- new(loggers)
#     +-- trace(message, fields, operation)
#     `-- warn(message, fields, operation)
"""Composite logger multiplexing calls to multiple child loggers."""

from __future__ import annotations

from collections.abc import Mapping, Sequence

from nyssa_ai_logging.log_level import LogLevel
from nyssa_ai_logging.logger import Logger


class CompositeLogger:
    """Dispatches log events to multiple configured loggers."""

    def __init__(self, loggers: Sequence[Logger]) -> None:
        """Initialize with a sequence of child loggers."""
        self._loggers = tuple(loggers)

    def is_enabled(self, level: LogLevel) -> bool:
        """Return True when any child logger is enabled for level."""
        return any(logger.is_enabled(level) for logger in self._loggers)

    def trace(
        self,
        message: str,
        fields: Mapping[str, object] | None = None,
        operation: str = "",
    ) -> None:
        """Forward trace record to all child loggers."""
        for logger in self._loggers:
            logger.trace(message, fields, operation)

    def debug(
        self,
        message: str,
        fields: Mapping[str, object] | None = None,
        operation: str = "",
    ) -> None:
        """Forward debug record to all child loggers."""
        for logger in self._loggers:
            logger.debug(message, fields, operation)

    def info(
        self,
        message: str,
        fields: Mapping[str, object] | None = None,
        operation: str = "",
    ) -> None:
        """Forward info record to all child loggers."""
        for logger in self._loggers:
            logger.info(message, fields, operation)

    def warn(
        self,
        message: str,
        fields: Mapping[str, object] | None = None,
        operation: str = "",
    ) -> None:
        """Forward warn record to all child loggers."""
        for logger in self._loggers:
            logger.warn(message, fields, operation)

    def error(
        self,
        message: str,
        error: BaseException | None = None,
        fields: Mapping[str, object] | None = None,
        operation: str = "",
    ) -> None:
        """Forward error record to all child loggers."""
        for logger in self._loggers:
            logger.error(message, error, fields, operation)

    def critical(
        self,
        message: str,
        error: BaseException | None = None,
        fields: Mapping[str, object] | None = None,
        operation: str = "",
    ) -> None:
        """Forward critical record to all child loggers."""
        for logger in self._loggers:
            logger.critical(message, error, fields, operation)
