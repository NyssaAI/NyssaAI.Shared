# Responsibility: Default system clock implementation returning current UTC time.
# Public interface:
# `-- SystemClock
#     `-- now()
"""Default system clock providing current UTC time."""

from __future__ import annotations

from datetime import UTC, datetime


class SystemClock:
    """Default clock returning current system UTC datetime."""

    def now(self) -> datetime:
        """Return the current UTC datetime."""
        return datetime.now(UTC)
