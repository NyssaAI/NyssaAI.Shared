# Responsibility: Define structured logger protocol and connection string redaction for database gateway.
# Public interface:
# +-- DbGatewayLogger
# |   +-- debug(operation, details, error)
# |   +-- error(operation, details, error)
# |   `-- info(operation, details)
# +-- PASSWORD_KEY_REGEX
# +-- PASSWORD_URI_REGEX
# +-- emit_gateway_log(logger, level, operation, details, error)
# `-- redact_connection_string(connection_string)
"""Logging protocols, standard library logging bridge, and credentials redaction helper."""

import logging
import re
from typing import Mapping, Protocol

PASSWORD_KEY_REGEX = re.compile(
    r"(?i)(password|pwd|user id|uid|secret)\s*=\s*([^;]+)"
)
PASSWORD_URI_REGEX = re.compile(
    r"(?i)(postgres(?:ql)?://[^:]+:)([^@]+)(@)"
)

_STDLIB_LOGGER = logging.getLogger("nyssa_ai_dbgateway")


def redact_connection_string(connection_string: str) -> str:
    """Redact secret/password values from connection strings or URIs."""
    if not connection_string:
        return ""
    result = PASSWORD_KEY_REGEX.sub(r"\1=***", connection_string)
    return PASSWORD_URI_REGEX.sub(r"\1***\3", result)


class DbGatewayLogger(Protocol):
    """Protocol for logging operational database metrics and diagnostics."""

    def info(self, operation: str, details: Mapping[str, object]) -> None:
        """Log operational success or state transition."""
        ...

    def debug(self, operation: str, details: Mapping[str, object], error: BaseException | None = None) -> None:
        """Log low-level query execution and latency diagnostics."""
        ...

    def error(self, operation: str, details: Mapping[str, object], error: BaseException | None = None) -> None:
        """Log execution failure with redacted diagnostic metadata."""
        ...


def emit_gateway_log(
    logger: DbGatewayLogger | logging.Logger | None,
    level: str,
    operation: str,
    details: Mapping[str, object],
    error: BaseException | None = None,
) -> None:
    """Emit structured database gateway diagnostics to an optional protocol logger or standard Python logging."""
    try:
        if logger is not None and not isinstance(logger, logging.Logger):
            if level == "info":
                logger.info(operation, details)
            elif level == "error":
                logger.error(operation, details, error)
            else:
                logger.debug(operation, details, error)
            return

        std_logger = logger if isinstance(logger, logging.Logger) else _STDLIB_LOGGER
        extra_fields: dict[str, object] = {"operation": operation, **details}
        if error is not None:
            extra_fields["error_type"] = type(error).__name__
            extra_fields["error_message"] = redact_connection_string(str(error))

        if level == "info":
            if std_logger.isEnabledFor(logging.INFO):
                std_logger.info(
                    "Database operation {operation} completed ({outcome})",
                    extra=extra_fields,
                )
        elif level == "error":
            if std_logger.isEnabledFor(logging.ERROR):
                std_logger.error(
                    "Database operation {operation} failed ({error_type})",
                    extra=extra_fields,
                )
        else:
            if std_logger.isEnabledFor(logging.DEBUG):
                std_logger.debug(
                    "Database operation {operation} ({outcome})",
                    extra=extra_fields,
                )
    except Exception:
        # Sink failure containment: logging errors must never mask database outcomes.
        pass

