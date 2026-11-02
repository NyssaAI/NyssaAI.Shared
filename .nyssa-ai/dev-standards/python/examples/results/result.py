# Responsibility: Represent success or a caller-actionable failure without exception control flow.
# Public interface:
# +-- Err
# |   +-- error
# |   +-- map(fn)
# |   +-- ok
# |   `-- then(fn)
# +-- Ok
# |   +-- map(fn)
# |   +-- ok
# |   +-- then(fn)
# |   `-- value
# +-- Result
# +-- fail(error)
# `-- ok(value)
"""Discriminated-union Result types for expected domain and operational outcomes."""

from collections.abc import Callable
from dataclasses import dataclass
from typing import Literal


@dataclass(frozen=True, slots=True)
class Ok[T]:
    """Ok holds a successfully produced value."""

    value: T
    ok: Literal[True] = True

    def map[U](self, fn: Callable[[T], U]) -> "Ok[U]":
        """Transform the contained value using a mapping function."""
        return Ok(value=fn(self.value))

    def then[U, E](self, fn: Callable[[T], "Result[U, E]"]) -> "Result[U, E]":
        """Chain another fallible operation on the contained value."""
        return fn(self.value)


@dataclass(frozen=True, slots=True)
class Err[E]:
    """Err holds a caller-actionable failure code or structured error."""

    error: E
    ok: Literal[False] = False

    def map[U](self, fn: object) -> "Err[E]":
        """Propagate the existing failure without invoking the mapping function."""
        return self

    def then[U](self, fn: object) -> "Err[E]":
        """Propagate the existing failure without invoking the chained operation."""
        return self


type Result[T, E] = Ok[T] | Err[E]


def ok[T](value: T) -> Ok[T]:
    """Wrap a successful value in an immutable Ok arm."""
    return Ok(value=value)


def fail[E](error: E) -> Err[E]:
    """Wrap a stable failure value in an immutable Err arm."""
    return Err(error=error)
