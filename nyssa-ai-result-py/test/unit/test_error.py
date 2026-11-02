"""Unit tests for Error and ErrorCategory."""

from __future__ import annotations

import asyncio
from typing import Any

import pytest

from nyssa_ai_result.error import (
    CANCELED_USER_MESSAGE,
    DEFAULT_USER_MESSAGE,
    TIMED_OUT_USER_MESSAGE,
    Error,
)
from nyssa_ai_result.error_category import ErrorCategory
from nyssa_ai_result.result_exception import ResultException


def test_error_from_message_sets_user_and_technical_messages() -> None:
    error = Error.from_message("Invalid configuration", ErrorCategory.VALIDATION)

    assert error.user_message == "Invalid configuration"
    assert error.technical_message == "Invalid configuration"
    assert error.category == ErrorCategory.VALIDATION
    assert error.stack_trace is None
    assert error.has_stack_trace is False
    assert error.message == "Invalid configuration"


def test_error_from_message_with_none_uses_default() -> None:
    error = Error.from_message(None)

    assert error.user_message == DEFAULT_USER_MESSAGE
    assert error.technical_message == DEFAULT_USER_MESSAGE
    assert error.category == ErrorCategory.UNEXPECTED


def test_error_from_messages_preserves_distinct_messages() -> None:
    error = Error.from_messages(
        user_message="Service unavailable",
        technical_message="Connection timeout connecting to 10.0.0.1:5432",
        category=ErrorCategory.UNAVAILABLE,
    )

    assert error.user_message == "Service unavailable"
    assert error.technical_message == "Connection timeout connecting to 10.0.0.1:5432"
    assert error.category == ErrorCategory.UNAVAILABLE
    assert error.message == "Connection timeout connecting to 10.0.0.1:5432"


def test_error_from_exception_captures_chain_and_trace() -> None:
    try:
        try:
            raise ValueError("Root cause")
        except ValueError as inner:
            raise RuntimeError("Higher level failure") from inner
    except RuntimeError as ex:
        error = Error.from_exception(ex, user_message="Something went wrong", category=ErrorCategory.UNEXPECTED)

        assert error.user_message == "Something went wrong"
        assert "ValueError: Root cause" in error.technical_message
        assert "RuntimeError: Higher level failure" in error.technical_message
        assert error.stack_trace is not None
        assert "raise RuntimeError" in error.stack_trace
        assert error.has_stack_trace is True


def test_error_from_exception_none_creates_sentinel() -> None:
    error = Error.from_exception(None)

    assert error.user_message == DEFAULT_USER_MESSAGE
    assert "without any exception detail" in error.technical_message
    assert error.stack_trace is None
    assert error.has_stack_trace is False


def test_error_from_exceptions_flattens_exception_group() -> None:
    eg = ExceptionGroup("Multiple errors", [ValueError("Err 1"), KeyError("Err 2")])
    errors = Error.from_exceptions(eg, user_message="Batch failed")

    assert len(errors) == 2
    assert "ValueError: Err 1" in errors[0].technical_message
    assert "KeyError: 'Err 2'" in errors[1].technical_message


def test_error_from_exceptions_unwraps_result_exception() -> None:
    original_errors = (
        Error.from_message("First failure", ErrorCategory.VALIDATION),
        Error.from_message("Second failure", ErrorCategory.CONFLICT),
    )
    res_exc = ResultException(original_errors)

    unwrapped = Error.from_exceptions(res_exc)
    assert unwrapped == original_errors


def test_error_from_cancellation_classifies_by_evidence() -> None:
    canceled_err = Error.from_cancellation(asyncio.CancelledError())
    assert canceled_err.category == ErrorCategory.CANCELED
    assert canceled_err.user_message == CANCELED_USER_MESSAGE

    timeout_err = Error.from_cancellation(TimeoutError("Request timed out"))
    assert timeout_err.category == ErrorCategory.UNAVAILABLE
    assert timeout_err.user_message == TIMED_OUT_USER_MESSAGE


def test_error_at_least_one_replaces_empty_with_sentinel() -> None:
    sentinel = Error.at_least_one([])
    assert len(sentinel) == 1
    assert sentinel[0].user_message == DEFAULT_USER_MESSAGE
    assert "without any error" in sentinel[0].technical_message

    sentinel_none = Error.at_least_one(None)
    assert len(sentinel_none) == 1
    assert "without any error" in sentinel_none[0].technical_message

    valid_err = Error.from_message("Valid error")
    result = Error.at_least_one([None, valid_err, None])
    assert result == (valid_err,)
