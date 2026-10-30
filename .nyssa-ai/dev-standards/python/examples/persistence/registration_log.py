# Responsibility: Serialize allowlisted registration outcomes and contain synchronous diagnostic sink failures.
# Public interface:
# `-- RegistrationLog
#     +-- completed(correlation_id, outcome, elapsed_ms, code, attempts)
#     +-- lost_records
#     +-- new(sink, version, now)
#     `-- slow(correlation_id, elapsed_ms)
"""Bounded operational logger for email registration outcomes."""

import json
import re
from collections.abc import Callable
from datetime import UTC, datetime
from typing import Final

from persistence.types import RegistrationCode, RegistrationOutcome

_SAFE_TOKEN: Final[re.Pattern[str]] = re.compile(r"^[A-Za-z0-9._-]{1,64}$")


class RegistrationLog:
    """RegistrationLog borrows a synchronous, nonblocking host sink and tracks sink losses."""

    def __init__(
        self,
        sink: Callable[[str], None],
        version: str,
        now: Callable[[], datetime] = lambda: datetime.now(UTC),
    ) -> None:
        """Initialize with a borrowed sink, validated deployment label, and UTC clock."""
        if not _SAFE_TOKEN.match(version):
            raise ValueError("Invalid deployment version")
        self._sink = sink
        self._version = version
        self._now = now
        self._lost = 0

    @property
    def lost_records(self) -> int:
        """Return the cumulative count of records lost to sink failures or invalid tokens."""
        return self._lost

    def completed(
        self,
        correlation_id: str,
        outcome: RegistrationOutcome,
        elapsed_ms: float,
        code: RegistrationCode,
        attempts: int,
    ) -> None:
        """Emit one terminal outcome record without raw inputs or exception payloads."""
        level = "error" if outcome == "failure" else "info"
        self._write("registration.completed", level, correlation_id, outcome, elapsed_ms, code, attempts)

    def slow(self, correlation_id: str, elapsed_ms: float) -> None:
        """Emit a warning when database execution exceeds the provisional 250 ms threshold."""
        self._write(
            "registration.slow",
            "warn",
            correlation_id,
            "degraded",
            elapsed_ms,
            "dependency.slow",
            1,
        )

    def _write(
        self,
        event: str,
        level: str,
        correlation_id: str,
        outcome: str,
        elapsed_ms: float,
        code: str,
        attempts: int,
    ) -> None:
        if not _SAFE_TOKEN.match(correlation_id) or attempts not in (0, 1):
            self._lost += 1
            return
        try:
            payload = json.dumps(
                {
                    "timestamp": self._now().isoformat(),
                    "event": event,
                    "level": level,
                    "service": "registration-example",
                    "version": self._version,
                    "operation": "email.register",
                    "correlation_id": correlation_id,
                    "outcome": outcome,
                    "elapsed_ms": max(0.0, elapsed_ms),
                    "dependency": "postgres",
                    "attempts": attempts,
                    "code": code,
                },
                separators=(",", ":"),
            )
            self._sink(payload + "\n")
        except Exception:
            self._lost += 1
