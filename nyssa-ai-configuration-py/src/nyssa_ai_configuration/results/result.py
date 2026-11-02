# Responsibility: Adapt nyssa_ai_result primitives with ConfigurationError defaults and helpers.
# Public interface:
# +-- Result
# +-- fail(message, category, cause)
# `-- fail_error(error)
"""Result types and helpers for expected configuration domain and operational outcomes."""

from nyssa_ai_configuration.results.types import ConfigurationError, ErrorCategory
from nyssa_ai_result import Err, Ok
from nyssa_ai_result import Result as BaseResult
from nyssa_ai_result import ok

type Result[T, E = ConfigurationError] = BaseResult[T, E]

__all__ = [
    "Err",
    "Ok",
    "Result",
    "fail",
    "fail_error",
    "ok",
]


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
