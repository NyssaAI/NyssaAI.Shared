"""Deterministic clock for unit testing."""

from __future__ import annotations

from datetime import UTC, datetime, timedelta


class FixedClock:
    """Fixed clock returning a controlled datetime."""

    def __init__(self, initial_time: datetime | None = None) -> None:
        """Initialize with fixed UTC datetime."""
        self._current_time = initial_time if initial_time is not None else datetime(2026, 10, 13, 12, 0, 0, tzinfo=UTC)

    def now(self) -> datetime:
        """Return the current simulated datetime."""
        return self._current_time

    def advance(self, delta: timedelta) -> None:
        """Advance time by delta."""
        self._current_time += delta

    def advance_days(self, days: int) -> None:
        """Advance time by days."""
        self.advance(timedelta(days=days))
