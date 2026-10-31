# Responsibility: Define the operational logger protocol injected into action-taking configuration classes.
# Public interface:
# `-- ConfigurationLogger
#     +-- completed(operation, outcome, elapsed_ms, code, dependency, attempts, correlation_id)
#     +-- debug(operation, step, elapsed_ms, dependency, correlation_id)
#     `-- degraded(operation, code, elapsed_ms, dependency, attempts, correlation_id)
"""Protocol for operational boundary logging and debug telemetry."""

from typing import Protocol

from nyssa_ai_configuration.logging.types import ConfigurationLogOutcome


class ConfigurationLogger(Protocol):
    """Narrow operational logger contract owned by the configuration library."""

    def completed(
        self,
        operation: str,
        outcome: ConfigurationLogOutcome,
        elapsed_ms: float,
        code: str = "none",
        dependency: str = "local",
        attempts: int = 1,
        correlation_id: str = "default",
    ) -> None:
        """Emit one terminal outcome record for a configuration operation."""
        ...

    def degraded(
        self,
        operation: str,
        code: str,
        elapsed_ms: float,
        dependency: str = "local",
        attempts: int = 1,
        correlation_id: str = "default",
    ) -> None:
        """Emit a warning record when recoverable degradation (such as lock contention or retry) occurs."""
        ...

    def debug(
        self,
        operation: str,
        step: str,
        elapsed_ms: float = 0.0,
        dependency: str = "local",
        correlation_id: str = "default",
    ) -> None:
        """Emit fine-grained step telemetry when debug mode is enabled."""
        ...
