# Responsibility: Structured log record dataclass matching the canonical Nyssa JSON schema.
# Public interface:
# `-- LogRecord
#     +-- AdditionalFields
#     +-- Exception
#     +-- Level
#     +-- Message
#     +-- Module
#     +-- Operation
#     `-- Timestamp
"""Structured log record representation."""

from __future__ import annotations

from collections.abc import Mapping
from dataclasses import dataclass, field


@dataclass(frozen=True, slots=True)
class LogRecord:
    """Structured log record conforming to the canonical Nyssa format."""

    Timestamp: str
    Level: str
    Message: str
    Module: str
    Operation: str
    Exception: str | None
    AdditionalFields: Mapping[str, object] = field(default_factory=dict)
