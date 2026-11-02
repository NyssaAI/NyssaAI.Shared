# Responsibility: Provide railway-oriented discriminated-union Result with pure combinators.
# Public interface:
# +-- Err
# |   +-- category
# |   +-- error
# |   +-- errors
# |   +-- is_failure
# |   +-- is_success
# |   +-- message
# |   +-- ok
# |   +-- resolve(on_success, on_failure)
# |   +-- resolve_async(on_success, on_failure)
# |   +-- then(next_operation)
# |   +-- then_async(next_operation)
# |   +-- throw_if_error()
# |   +-- transform(transformation)
# |   +-- transform_async(transformation)
# |   +-- try_get_value()
# |   `-- unwrap()
# +-- Ok
# |   +-- category
# |   +-- error
# |   +-- errors
# |   +-- is_failure
# |   +-- is_success
# |   +-- message
# |   +-- ok
# |   +-- resolve(on_success, on_failure)
# |   +-- resolve_async(on_success, on_failure)
# |   +-- then(next_operation)
# |   +-- then_async(next_operation)
# |   +-- throw_if_error()
# |   +-- transform(transformation)
# |   +-- transform_async(transformation)
# |   +-- try_get_value()
# |   +-- unwrap()
# |   `-- value
# +-- Result
# +-- fail(error_or_errors, category, cause)
# +-- fail_error(error)
# +-- ok(value)
# +-- try_(action, error_handler, cancellation_token)
# `-- try_async(action, error_handler, cancellation_token)
"""Discriminated-union Result types for railway-oriented functional error handling."""

from __future__ import annotations

import asyncio
from collections.abc import Awaitable, Callable, Iterable
from dataclasses import dataclass
from typing import Literal, NoReturn

from nyssa_ai_result.error import Error
from nyssa_ai_result.error_category import ErrorCategory
from nyssa_ai_result.result_exception import ResultException


@dataclass(frozen=True, slots=True)
class Ok[T]:
    """Ok holds a successfully produced value."""

    value: T
    ok: Literal[True] = True

    @property
    def is_success(self) -> bool:
        return True

    @property
    def is_failure(self) -> bool:
        return False

    @property
    def errors(self) -> tuple[Error, ...]:
        return ()

    @property
    def error(self) -> None:
        return None

    @property
    def category(self) -> ErrorCategory:
        return ErrorCategory.UNEXPECTED

    @property
    def message(self) -> str:
        return ""

    def __bool__(self) -> bool:
        return True

    def throw_if_error(self) -> T:
        return self.value

    def unwrap(self) -> T:
        return self.value

    def try_get_value(self) -> tuple[Literal[True], T]:
        return True, self.value

    def then[U, E](self, next_operation: Callable[[T], Result[U, E]]) -> Result[U, E]:
        if next_operation is None:
            raise ValueError("next_operation cannot be None")
        return next_operation(self.value)

    async def then_async[U, E](self, next_operation: Callable[[T], Awaitable[Result[U, E]]]) -> Result[U, E]:
        if next_operation is None:
            raise ValueError("next_operation cannot be None")
        return await next_operation(self.value)

    def transform[U](self, transformation: Callable[[T], U]) -> Ok[U]:
        if transformation is None:
            raise ValueError("transformation cannot be None")
        return Ok(value=transformation(self.value))

    async def transform_async[U](self, transformation: Callable[[T], Awaitable[U]]) -> Ok[U]:
        if transformation is None:
            raise ValueError("transformation cannot be None")
        return Ok(value=await transformation(self.value))

    def resolve[R](self, on_success: Callable[[T], R], on_failure: Callable[[tuple[Error, ...]], R]) -> R:
        if on_success is None or on_failure is None:
            raise ValueError("resolve handlers cannot be None")
        return on_success(self.value)

    async def resolve_async[R](
        self,
        on_success: Callable[[T], Awaitable[R]],
        on_failure: Callable[[tuple[Error, ...]], Awaitable[R]],
    ) -> R:
        if on_success is None or on_failure is None:
            raise ValueError("resolve handlers cannot be None")
        return await on_success(self.value)


@dataclass(frozen=True, slots=True)
class Err[E = Error]:
    """Err holds a caller-actionable failure code or structured error."""

    error: E
    ok: Literal[False] = False

    @property
    def is_success(self) -> bool:
        return False

    @property
    def is_failure(self) -> bool:
        return True

    @property
    def errors(self) -> tuple[Error, ...]:
        if isinstance(self.error, Error):
            return (self.error,)
        if isinstance(self.error, tuple):
            return self.error
        if hasattr(self.error, "message"):
            msg = getattr(self.error, "message")
            cat = getattr(self.error, "category", ErrorCategory.UNEXPECTED)
            cause_type = getattr(self.error, "cause_type", None)
            return (Error.from_messages(user_message=str(msg), category=cat, cause_type=cause_type),)
        return (Error.from_message(str(self.error)),)

    @property
    def category(self) -> ErrorCategory | str:
        if isinstance(self.error, Error):
            return self.error.category
        if hasattr(self.error, "category"):
            return getattr(self.error, "category")  # type: ignore[no-any-return]
        return ErrorCategory.UNEXPECTED

    @property
    def message(self) -> str:
        if isinstance(self.error, Error):
            return self.error.message
        if hasattr(self.error, "message"):
            return str(getattr(self.error, "message"))
        return str(self.error)

    def __bool__(self) -> bool:
        return False

    def throw_if_error(self) -> NoReturn:
        raise ResultException(self.errors)

    def unwrap(self) -> NoReturn:
        raise ResultException(self.errors)

    def try_get_value(self) -> tuple[Literal[False], None]:
        return False, None

    def then(self, next_operation: object) -> Err[E]:
        if next_operation is None:
            raise ValueError("next_operation cannot be None")
        return self

    async def then_async(self, next_operation: object) -> Err[E]:
        if next_operation is None:
            raise ValueError("next_operation cannot be None")
        return self

    def transform(self, transformation: object) -> Err[E]:
        if transformation is None:
            raise ValueError("transformation cannot be None")
        return self

    async def transform_async(self, transformation: object) -> Err[E]:
        if transformation is None:
            raise ValueError("transformation cannot be None")
        return self

    def resolve[R](self, on_success: object, on_failure: Callable[[tuple[Error, ...]], R]) -> R:
        if on_success is None or on_failure is None:
            raise ValueError("resolve handlers cannot be None")
        return on_failure(self.errors)

    async def resolve_async[R](
        self,
        on_success: object,
        on_failure: Callable[[tuple[Error, ...]], Awaitable[R]],
    ) -> R:
        if on_success is None or on_failure is None:
            raise ValueError("resolve handlers cannot be None")
        return await on_failure(self.errors)


type Result[T, E = Error] = Ok[T] | Err[E]


def ok[T](value: T) -> Ok[T]:
    """Wrap a successful value in an immutable Ok arm."""
    return Ok(value=value)


def fail(
    error_or_errors: str | Error | Iterable[Error | None] | None = None,
    category: ErrorCategory | str = ErrorCategory.VALIDATION,
    cause: BaseException | None = None,
) -> Err[Error]:
    """Wrap a message, category, or error in an immutable Err arm."""
    if isinstance(error_or_errors, Error):
        return Err(error=error_or_errors)
    if isinstance(error_or_errors, str):
        err = Error.from_messages(
            user_message=error_or_errors,
            technical_message=error_or_errors,
            category=category,
            cause_type=type(cause).__name__ if cause is not None else None,
        )
        return Err(error=err)
    if error_or_errors is not None:
        all_errs = Error.at_least_one(error_or_errors)
        return Err(error=all_errs[0])
    return Err(error=Error.from_message(None, category=category))


def fail_error[E](error: E) -> Err[E]:
    """Wrap an existing domain error in an immutable Err arm."""
    return Err(error=error)


def try_[T](
    action: Callable[[], T],
    error_handler: Callable[[Exception], Error] | None = None,
    cancellation_token: object | None = None,
) -> Result[T, Error]:
    """Explicit boundary executing synchronous action and catching thrown exceptions."""
    if action is None:
        raise ValueError("action cannot be None")
    try:
        return Ok(value=action())
    except (KeyboardInterrupt, SystemExit):
        raise
    except (asyncio.CancelledError, GeneratorExit, TimeoutError) as cancel_exc:
        return Err(error=Error.from_cancellation(cancel_exc, cancellation_token))
    except Exception as exc:
        if error_handler is not None:
            try:
                return Err(error=error_handler(exc))
            except Exception as handler_exc:
                return Err(error=Error.from_caught(handler_exc, cancellation_token)[0])
        return Err(error=Error.from_caught(exc, cancellation_token)[0])


async def try_async[T](
    action: Callable[[], Awaitable[T]],
    error_handler: Callable[[Exception], Error] | None = None,
    cancellation_token: object | None = None,
) -> Result[T, Error]:
    """Explicit boundary executing asynchronous action and catching thrown exceptions."""
    if action is None:
        raise ValueError("action cannot be None")
    try:
        return Ok(value=await action())
    except (KeyboardInterrupt, SystemExit):
        raise
    except (asyncio.CancelledError, GeneratorExit, TimeoutError) as cancel_exc:
        return Err(error=Error.from_cancellation(cancel_exc, cancellation_token))
    except Exception as exc:
        if error_handler is not None:
            try:
                return Err(error=error_handler(exc))
            except Exception as handler_exc:
                return Err(error=Error.from_caught(handler_exc, cancellation_token)[0])
        return Err(error=Error.from_caught(exc, cancellation_token)[0])
