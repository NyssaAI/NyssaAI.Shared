# Responsibility: Define error categories and immutable structured configuration error records.
# Public interface:
# +-- ConfigurationError
# |   +-- category
# |   +-- cause_type
# |   `-- message
# `-- ErrorCategory
"""Structured configuration error contracts."""

from dataclasses import dataclass
from typing import Literal

type ErrorCategory = Literal[
    "Validation",
    "Forbidden",
    "NotFound",
    "Conflict",
    "Unavailable",
    "Indeterminate",
    "Canceled",
    "Unexpected",
]


@dataclass(frozen=True, slots=True)
class ConfigurationError:
    """Immutable failure payload describing a configuration failure without leaking raw secrets."""

    message: str
    category: ErrorCategory = "Unexpected"
    cause_type: str | None = None
