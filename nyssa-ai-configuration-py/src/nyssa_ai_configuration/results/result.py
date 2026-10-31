# Responsibility: Represent success or a caller-actionable failure without exception control flow.
# Public interface:
# +-- Err
# |   +-- category
# |   +-- error
# |   +-- message
# |   `-- ok
# +-- Ok
# |   +-- ok
# |   `-- value
# +-- Result
# +-- fail(message, category, cause)
# +-- fail_error(error)
# `-- ok(value)
"""Discriminated-union Result types for expected configuration domain and operational outcomes."""

from dataclasses import dataclass
from typing import Literal

from nyssa_ai_configuration.results.types import ConfigurationError, ErrorCategory


@dataclass(frozen=True, slots=True)
class Ok[T]:
    """Ok holds a successfully produced value."""

    value: T
    ok: Literal[True] = True


@dataclass(frozen=True, slots=True)
class Err[E = ConfigurationError]:
    """Err holds a caller-actionable structured error."""

    error: E
    ok: Literal[False] = False

    @property
    def message(self) -> str:
        """Return the error message when error is a ConfigurationError."""
        if isinstance(self.error, ConfigurationError):
            return self.error.message
        return str(self.error)

    @property
    def category(self) -> ErrorCategory:
        """Return the error category when error is a ConfigurationError."""
        if isinstance(self.error, ConfigurationError):
            return self.error.category
        return "Unexpected"


type Result[T, E = ConfigurationError] = Ok[T] | Err[E]


def ok[T](value: T) -> Ok[T]:
    """Wrap a successful value in an immutable Ok arm."""
    return Ok(value=value)


def fail(
    message: str,
    category: ErrorCategory = "Validation",
    cause: BaseException | None = None,
) -> Err[ConfigurationError]:
    """Wrap a message and category in an immutable Err[ConfigurationError] arm."""
    cause_type = type(cause).__name__ if cause is not None else None
    return Err(error=ConfigurationError(message=message, category=category, cause_type=cause_type))


def fail_error[E](error: E) -> Err[E]:
    """Wrap an existing error instance in an immutable Err arm."""
    return Err(error=error)
