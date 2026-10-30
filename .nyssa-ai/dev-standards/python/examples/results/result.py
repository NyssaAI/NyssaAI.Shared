# Responsibility: Represent success or a caller-actionable failure without exception control flow.
# Public interface:
# +-- Err
# |   +-- error
# |   `-- ok
# +-- Ok
# |   +-- ok
# |   `-- value
# +-- Result
# +-- fail(error)
# `-- ok(value)
"""Discriminated-union Result types for expected domain and operational outcomes."""

from dataclasses import dataclass
from typing import Literal


@dataclass(frozen=True, slots=True)
class Ok[T]:
    """Ok holds a successfully produced value."""

    value: T
    ok: Literal[True] = True


@dataclass(frozen=True, slots=True)
class Err[E]:
    """Err holds a caller-actionable failure code or structured error."""

    error: E
    ok: Literal[False] = False


type Result[T, E] = Ok[T] | Err[E]


def ok[T](value: T) -> Ok[T]:
    """Wrap a successful value in an immutable Ok arm."""
    return Ok(value=value)


def fail[E](error: E) -> Err[E]:
    """Wrap a stable failure value in an immutable Err arm."""
    return Err(error=error)
