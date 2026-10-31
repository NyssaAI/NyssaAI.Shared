# Responsibility: Time abstraction protocol for log timestamps and retention calculations.
# Public interface:
# `-- Clock
#     `-- now()
"""Clock protocol for deterministic time testing."""

from __future__ import annotations

from datetime import datetime
from typing import Protocol


class Clock(Protocol):
    """Protocol for providing current UTC datetime."""

    def now(self) -> datetime:
        """Return the current UTC datetime."""
        ...
