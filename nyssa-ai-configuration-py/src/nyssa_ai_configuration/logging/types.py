# Responsibility: Define structured operational log record and level types for configuration boundaries.
# Public interface:
# +-- ConfigurationLogLevel
# +-- ConfigurationLogOutcome
# `-- ConfigurationLogRecord
#     +-- attempts
#     +-- code
#     +-- correlation_id
#     +-- dependency
#     +-- elapsed_ms
#     +-- event
#     +-- level
#     +-- operation
#     +-- outcome
#     +-- service
#     +-- timestamp
#     `-- version
"""Immutable operational log record contracts."""

from dataclasses import dataclass
from typing import Literal

type ConfigurationLogLevel = Literal["debug", "info", "warn", "error"]
type ConfigurationLogOutcome = Literal["success", "rejected", "degraded", "failure", "cancelled"]


@dataclass(frozen=True, slots=True)
class ConfigurationLogRecord:
    """Allowlisted operational boundary log record free of secrets or raw exception payloads."""

    timestamp: str
    level: ConfigurationLogLevel
    event: str
    service: str
    version: str
    correlation_id: str
    operation: str
    outcome: ConfigurationLogOutcome
    elapsed_ms: float
    code: str = "none"
    dependency: str = "local"
    attempts: int = 1
