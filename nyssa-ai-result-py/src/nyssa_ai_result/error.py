# Responsibility: Represent failure information as an immutable value.
# Public interface:
# +-- CANCELED_USER_MESSAGE
# +-- DEFAULT_USER_MESSAGE
# +-- Error
# |   +-- CANCELED_USER_MESSAGE
# |   +-- DEFAULT_USER_MESSAGE
# |   +-- TIMED_OUT_USER_MESSAGE
# |   +-- at_least_one(errors)
# |   +-- canceled(canceled)
# |   +-- category
# |   +-- cause_type
# |   +-- from_cancellation(canceled, caller_token)
# |   +-- from_caught(exception, caller_token)
# |   +-- from_exception(exception, user_message, category)
# |   +-- from_exceptions(exception_or_exceptions, user_message, category)
# |   +-- from_message(message, category)
# |   +-- from_messages(user_message, technical_message, category, cause_type)
# |   +-- has_stack_trace
# |   +-- message
# |   +-- stack_trace
# |   +-- technical_message
# |   `-- user_message
# `-- TIMED_OUT_USER_MESSAGE
"""Immutable description of a failure for functional error handling."""

from __future__ import annotations

import asyncio
import traceback
from collections.abc import Iterable
from dataclasses import dataclass

from nyssa_ai_result.error_category import ErrorCategory
from nyssa_ai_result.result_exception import ResultException

DEFAULT_USER_MESSAGE: str = "An unexpected error occurred"
CANCELED_USER_MESSAGE: str = "The operation was cancelled."
TIMED_OUT_USER_MESSAGE: str = "The operation timed out."
_MISSING_EXCEPTION_DETAIL: str = "A failure was reported without any exception detail"
_MISSING_ERROR_DETAIL: str = "A failure was reported without any error"


def _resolve_category(cat: ErrorCategory | str) -> ErrorCategory:
    if isinstance(cat, ErrorCategory):
        return cat
    normalized = str(cat).strip().lower().replace("_", "")
    for member in ErrorCategory:
        if member.value.lower() == normalized or member.name.lower() == normalized:
            return member
    return ErrorCategory.UNEXPECTED


def _describe_exception(exc: BaseException) -> str:
    chain: list[str] = []
    current: BaseException | None = exc
    seen: set[int] = set()
    while current is not None and id(current) not in seen:
        seen.add(id(current))
        chain.append(f"{type(current).__name__}: {current}")
        current = current.__cause__ or current.__context__
    return " ---> ".join(chain)


def _capture_stack_trace(exc: BaseException) -> str | None:
    if exc.__traceback__ is None:
        return None
    lines = traceback.format_exception(type(exc), exc, exc.__traceback__)
    formatted = "".join(lines).strip()
    return formatted if formatted else None


def _is_token_canceled(caller_token: object | None) -> bool:
    if caller_token is None:
        return False
    if isinstance(caller_token, bool):
        return caller_token
    if hasattr(caller_token, "is_cancellation_requested"):
        return bool(getattr(caller_token, "is_cancellation_requested"))
    if hasattr(caller_token, "is_set") and callable(getattr(caller_token, "is_set")):
        return bool(caller_token.is_set())
    return False


@dataclass(frozen=True, slots=True)
class Error:
    """Immutable failure details safe for boundaries and diagnostics."""

    user_message: str = DEFAULT_USER_MESSAGE
    technical_message: str | None = None
    category: ErrorCategory = ErrorCategory.UNEXPECTED
    stack_trace: str | None = None
    cause_type: str | None = None

    DEFAULT_USER_MESSAGE: str = DEFAULT_USER_MESSAGE
    CANCELED_USER_MESSAGE: str = CANCELED_USER_MESSAGE
    TIMED_OUT_USER_MESSAGE: str = TIMED_OUT_USER_MESSAGE

    @property
    def message(self) -> str:
        """Get diagnostic message if present, otherwise user message."""
        return self.technical_message if self.technical_message else self.user_message

    @property
    def has_stack_trace(self) -> bool:
        """Return True when a non-empty stack trace was captured."""
        return bool(self.stack_trace and self.stack_trace.strip())

    @classmethod
    def from_message(
        cls,
        message: str | None = None,
        category: ErrorCategory | str = ErrorCategory.UNEXPECTED,
    ) -> Error:
        """Create an error where message serves user and technical contexts."""
        resolved = message if message is not None else DEFAULT_USER_MESSAGE
        return cls(
            user_message=resolved,
            technical_message=resolved,
            category=_resolve_category(category),
            stack_trace=None,
            cause_type=None,
        )

    @classmethod
    def from_messages(
        cls,
        user_message: str | None = None,
        technical_message: str | None = None,
        category: ErrorCategory | str = ErrorCategory.UNEXPECTED,
        cause_type: str | None = None,
    ) -> Error:
        """Create an error with distinct user and technical messages."""
        user_res = user_message if user_message is not None else DEFAULT_USER_MESSAGE
        tech_res = technical_message if technical_message is not None else user_res
        return cls(
            user_message=user_res,
            technical_message=tech_res,
            category=_resolve_category(category),
            stack_trace=None,
            cause_type=cause_type,
        )

    @classmethod
    def from_exception(
        cls,
        exception: BaseException | None,
        user_message: str = DEFAULT_USER_MESSAGE,
        category: ErrorCategory | str = ErrorCategory.UNEXPECTED,
    ) -> Error:
        """Create an error capturing message chain and stack trace from an exception."""
        if exception is None:
            return cls(
                user_message=user_message,
                technical_message=_MISSING_EXCEPTION_DETAIL,
                category=_resolve_category(category),
                stack_trace=None,
                cause_type=None,
            )
        return cls(
            user_message=user_message,
            technical_message=_describe_exception(exception),
            category=_resolve_category(category),
            stack_trace=_capture_stack_trace(exception),
            cause_type=type(exception).__name__,
        )

    @classmethod
    def from_exceptions(
        cls,
        exception_or_exceptions: BaseException | Iterable[BaseException] | None,
        user_message: str = DEFAULT_USER_MESSAGE,
        category: ErrorCategory | str = ErrorCategory.UNEXPECTED,
    ) -> tuple[Error, ...]:
        """Expand an exception, group, or sequence into flat structured errors."""
        resolved_category = _resolve_category(category)
        if exception_or_exceptions is None:
            return (cls.from_exception(None, user_message, resolved_category),)

        if isinstance(exception_or_exceptions, ResultException):
            result_errors: tuple[Error, ...] = getattr(exception_or_exceptions, "errors", ())
            if result_errors:
                return result_errors

        if isinstance(exception_or_exceptions, BaseExceptionGroup):
            accumulated: list[Error] = []
            for nested in exception_or_exceptions.exceptions:
                accumulated.extend(cls.from_exceptions(nested, user_message, resolved_category))
            return tuple(accumulated) if accumulated else (cls.from_exception(exception_or_exceptions, user_message, resolved_category),)

        if isinstance(exception_or_exceptions, BaseException):
            return (cls.from_exception(exception_or_exceptions, user_message, resolved_category),)

        collected: list[Error] = []
        for exc in exception_or_exceptions:
            collected.extend(cls.from_exceptions(exc, user_message, resolved_category))
        return tuple(collected) if collected else (cls.from_exception(None, user_message, resolved_category),)

    @classmethod
    def canceled(cls, canceled: BaseException | None = None) -> Error:
        """Create a cancellation error carrying ErrorCategory.CANCELED."""
        tech = (
            f"The operation was cancelled before it finished: {type(canceled).__name__}"
            if canceled is not None
            else f"The operation was cancelled before it finished: {_MISSING_EXCEPTION_DETAIL}"
        )
        stack = _capture_stack_trace(canceled) if canceled is not None else None
        return cls(
            user_message=CANCELED_USER_MESSAGE,
            technical_message=tech,
            category=ErrorCategory.CANCELED,
            stack_trace=stack,
            cause_type=type(canceled).__name__ if canceled is not None else None,
        )

    @classmethod
    def from_cancellation(
        cls,
        canceled: BaseException | None = None,
        caller_token: object | None = None,
    ) -> Error:
        """Classify a cancellation by caller evidence versus timeout."""
        if canceled is None:
            return cls.canceled(None)
        if _is_token_canceled(caller_token):
            return cls.canceled(canceled)
        if isinstance(canceled, TimeoutError):
            return cls.from_exception(canceled, TIMED_OUT_USER_MESSAGE, ErrorCategory.UNAVAILABLE)
        if caller_token is not None and not _is_token_canceled(caller_token):
            return cls.from_exception(canceled, TIMED_OUT_USER_MESSAGE, ErrorCategory.UNAVAILABLE)
        return cls.canceled(canceled)

    @classmethod
    def from_caught(
        cls,
        exception: BaseException | None,
        caller_token: object | None = None,
    ) -> tuple[Error, ...]:
        """Translate any caught exception at an adapter boundary."""
        if exception is None:
            return (cls.from_exception(None),)
        if isinstance(exception, ResultException):
            result_errors: tuple[Error, ...] = getattr(exception, "errors", ())
            if result_errors:
                return result_errors
        if isinstance(exception, (asyncio.CancelledError, GeneratorExit, TimeoutError)):
            return (cls.from_cancellation(exception, caller_token),)
        return cls.from_exceptions(exception)

    @staticmethod
    def at_least_one(errors: Iterable[Error | None] | None) -> tuple[Error, ...]:
        """Normalize an error sequence, substituting a sentinel if empty."""
        if errors is None:
            return (
                Error(
                    user_message=DEFAULT_USER_MESSAGE,
                    technical_message=_MISSING_ERROR_DETAIL,
                    category=ErrorCategory.UNEXPECTED,
                    stack_trace=None,
                    cause_type=None,
                ),
            )
        valid = [e for e in errors if e is not None]
        if not valid:
            return (
                Error(
                    user_message=DEFAULT_USER_MESSAGE,
                    technical_message=_MISSING_ERROR_DETAIL,
                    category=ErrorCategory.UNEXPECTED,
                    stack_trace=None,
                    cause_type=None,
                ),
            )
        return tuple(valid)
