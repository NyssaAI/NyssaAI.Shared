# Responsibility: Sanitized metadata receipt returned after a secret has been stored.
# Public interface:
# `-- SecretReceipt
#     +-- key
#     +-- namespace
#     +-- provider
#     `-- stored_at
"""Sanitized secret receipt data model."""

from __future__ import annotations

import dataclasses
from datetime import UTC, datetime


@dataclasses.dataclass(frozen=True)
class SecretReceipt:
    """Sanitized metadata receipt returned after a secret has been stored."""

    namespace: str
    key: str
    provider: str
    stored_at: datetime = dataclasses.field(default_factory=lambda: datetime.now(UTC))
