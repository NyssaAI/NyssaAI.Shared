# Responsibility: Structured operational logging protocol for action-taking classes.
# Public interface:
# `-- Logger
#     +-- critical(message, error, fields, operation)
#     +-- debug(message, fields, operation)
#     +-- error(message, error, fields, operation)
#     +-- info(message, fields, operation)
#     +-- is_enabled(level)
#     +-- trace(message, fields, operation)
#     `-- warn(message, fields, operation)
"""Logger protocol for structured operational logging."""

from __future__ import annotations

from collections.abc import Mapping
from typing import Protocol

from nyssa_ai_logging.log_level import LogLevel


class Logger(Protocol):
    """Protocol defining structured operational logging methods."""

    def trace(
        self,
        message: str,
        fields: Mapping[str, object] | None = None,
        operation: str = "",
    ) -> None:
        """Log a fine-grained trace message."""
        ...

    def debug(
        self,
        message: str,
        fields: Mapping[str, object] | None = None,
        operation: str = "",
    ) -> None:
        """Log a diagnostic debug message."""
        ...

    def info(
        self,
        message: str,
        fields: Mapping[str, object] | None = None,
        operation: str = "",
    ) -> None:
        """Log an operational boundary information message."""
        ...

    def warn(
        self,
        message: str,
        fields: Mapping[str, object] | None = None,
        operation: str = "",
    ) -> None:
        """Log a recoverable degradation warning message."""
        ...

    def error(
        self,
        message: str,
        error: BaseException | None = None,
        fields: Mapping[str, object] | None = None,
        operation: str = "",
    ) -> None:
        """Log an operational failure message."""
        ...

    def critical(
        self,
        message: str,
        error: BaseException | None = None,
        fields: Mapping[str, object] | None = None,
        operation: str = "",
    ) -> None:
        """Log a critical failure message."""
        ...

    def is_enabled(self, level: LogLevel) -> bool:
        """Return True when the supplied level is enabled."""
        ...
