# Responsibility: Group pure immutable DTOs and outcome types for the email registration feature.
# Public interface:
# +-- FailureReason
# +-- RegistrationCode
# +-- RegistrationFailure
# |   +-- cause
# |   +-- error
# |   `-- ok
# +-- RegistrationOutcome
# +-- RegistrationResult
# `-- RegistrationSuccess
#     `-- ok
"""Pure data contracts and discriminated result types for email registration."""

from dataclasses import dataclass
from typing import Literal

type RegistrationOutcome = Literal[
    "success", "rejected", "failure", "cancelled", "degraded"
]
type RegistrationCode = Literal[
    "none",
    "email.invalid",
    "email.exists",
    "database.unavailable",
    "caller.cancelled",
    "dependency.slow",
]
type FailureReason = Literal[
    "email.invalid", "email.exists", "database.unavailable"
]


@dataclass(frozen=True, slots=True)
class RegistrationSuccess:
    """RegistrationSuccess indicates that a new normalized email was committed."""

    ok: Literal[True] = True


@dataclass(frozen=True, slots=True)
class RegistrationFailure:
    """RegistrationFailure reports a caller-actionable reason and retains the internal cause."""

    error: FailureReason
    cause: BaseException | None = None
    ok: Literal[False] = False


type RegistrationResult = RegistrationSuccess | RegistrationFailure
