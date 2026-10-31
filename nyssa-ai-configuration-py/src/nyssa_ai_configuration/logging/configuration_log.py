# Responsibility: Serialize allowlisted configuration operational outcomes and contain diagnostic sink failures.
# Public interface:
# `-- ConfigurationLog
#     +-- completed(operation, outcome, elapsed_ms, code, dependency, attempts, correlation_id)
#     +-- debug(operation, step, elapsed_ms, dependency, correlation_id)
#     +-- degraded(operation, code, elapsed_ms, dependency, attempts, correlation_id)
#     +-- lost_records
#     `-- new(sink, service, version, debug_enabled, now)
"""Bounded operational logger implementation with sink-failure containment."""

from collections.abc import Callable
from datetime import UTC, datetime
import json
import re
from typing import Final

from nyssa_ai_configuration.logging.types import (
    ConfigurationLogLevel,
    ConfigurationLogOutcome,
)

_SAFE_TOKEN: Final[re.Pattern[str]] = re.compile(r"^[A-Za-z0-9._:-]{1,128}$")


class ConfigurationLog:
    """ConfigurationLog borrows a synchronous sink, serializes allowlisted fields, and contains sink failures."""

    def __init__(
        self,
        sink: Callable[[str], None] | None = None,
        service: str = "nyssa-ai-configuration",
        version: str = "0.3.0",
        debug_enabled: bool = False,
        now: Callable[[], datetime] = lambda: datetime.now(UTC),
    ) -> None:
        """Initialize with an optional borrowed sink, service label, version, debug flag, and UTC clock."""
        self._sink = sink
        self._service = service if _SAFE_TOKEN.match(service) else "nyssa-ai-configuration"
        self._version = version if _SAFE_TOKEN.match(version) else "0.3.0"
        self._debug_enabled = debug_enabled
        self._now = now
        self._lost = 0

    @property
    def lost_records(self) -> int:
        """Return the cumulative count of records lost to sink exceptions or invalid tokens."""
        return self._lost

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
        """Emit one terminal outcome record without raw inputs or secret payloads."""
        level: ConfigurationLogLevel = "error" if outcome == "failure" else "info"
        self._write(
            event=f"{operation}.completed",
            level=level,
            operation=operation,
            outcome=outcome,
            elapsed_ms=elapsed_ms,
            code=code,
            dependency=dependency,
            attempts=attempts,
            correlation_id=correlation_id,
        )

    def degraded(
        self,
        operation: str,
        code: str,
        elapsed_ms: float,
        dependency: str = "local",
        attempts: int = 1,
        correlation_id: str = "default",
    ) -> None:
        """Emit a warning when lock contention, retries, or fallback degradation occurs."""
        self._write(
            event=f"{operation}.degraded",
            level="warn",
            operation=operation,
            outcome="degraded",
            elapsed_ms=elapsed_ms,
            code=code,
            dependency=dependency,
            attempts=attempts,
            correlation_id=correlation_id,
        )

    def debug(
        self,
        operation: str,
        step: str,
        elapsed_ms: float = 0.0,
        dependency: str = "local",
        correlation_id: str = "default",
    ) -> None:
        """Emit debug step telemetry when debug_enabled is True."""
        if not self._debug_enabled:
            return
        self._write(
            event=f"{operation}.debug",
            level="debug",
            operation=operation,
            outcome="success",
            elapsed_ms=elapsed_ms,
            code=step,
            dependency=dependency,
            attempts=1,
            correlation_id=correlation_id,
        )

    def _write(
        self,
        event: str,
        level: ConfigurationLogLevel,
        operation: str,
        outcome: ConfigurationLogOutcome,
        elapsed_ms: float,
        code: str,
        dependency: str,
        attempts: int,
        correlation_id: str,
    ) -> None:
        if self._sink is None:
            return
        if (
            not _SAFE_TOKEN.match(operation)
            or not _SAFE_TOKEN.match(code)
            or not _SAFE_TOKEN.match(dependency)
            or not _SAFE_TOKEN.match(correlation_id)
            or attempts < 1
        ):
            self._lost += 1
            return
        try:
            payload = json.dumps(
                {
                    "timestamp": self._now().isoformat(),
                    "level": level,
                    "event": event,
                    "service": self._service,
                    "version": self._version,
                    "correlation_id": correlation_id,
                    "operation": operation,
                    "outcome": outcome,
                    "elapsed_ms": round(max(0.0, elapsed_ms), 3),
                    "dependency": dependency,
                    "attempts": attempts,
                    "code": code,
                },
                separators=(",", ":"),
            )
            self._sink(payload)
        except Exception:
            self._lost += 1
