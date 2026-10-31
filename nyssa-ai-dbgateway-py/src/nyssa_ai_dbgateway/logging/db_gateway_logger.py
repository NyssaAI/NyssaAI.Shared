# Responsibility: Define structured logger protocol and diagnostic record types for database gateway.
# Public interface:
# +-- DbGatewayLogger
# |   +-- debug(operation, details, error)
# |   +-- error(operation, details, error)
# |   `-- info(operation, details)
# +-- PASSWORD_KEY_REGEX
# +-- PASSWORD_URI_REGEX
# `-- redact_connection_string(connection_string)
"""Logging protocols and credentials redaction helper."""

import re
from typing import Mapping, Protocol

PASSWORD_KEY_REGEX = re.compile(
    r"(?i)(password|pwd|user id|uid|secret)\s*=\s*([^;]+)"
)
PASSWORD_URI_REGEX = re.compile(
    r"(?i)(postgres(?:ql)?://[^:]+:)([^@]+)(@)"
)


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
