"""Unit tests for try_ and try_async explicit exception boundaries."""

from __future__ import annotations

import asyncio
import threading
from typing import NoReturn
import pytest

from nyssa_ai_result.error import Error
from nyssa_ai_result.error_category import ErrorCategory
from nyssa_ai_result.result import try_, try_async


def test_try_action_success_returns_ok() -> None:
    res = try_(lambda: 100 // 2)

    assert res.is_success is True
    assert res.value == 50


def test_try_action_throws_returns_failure_with_caught_error() -> None:
    res = try_(lambda: 10 // 0)

    assert res.is_failure is True
    assert len(res.errors) == 1
    assert "ZeroDivisionError" in res.errors[0].technical_message
    assert res.errors[0].category == ErrorCategory.UNEXPECTED


def test_try_with_error_handler_translates_thrown_exception() -> None:
    def handler(ex: Exception) -> Error:
        return Error.from_messages(
            user_message="Invalid calculation input",
            technical_message=f"Math error: {ex}",
            category=ErrorCategory.VALIDATION,
        )

    res = try_(lambda: 10 // 0, error_handler=handler)

    assert res.is_failure is True
    assert res.errors[0].user_message == "Invalid calculation input"
    assert "division or modulo by zero" in res.errors[0].technical_message
    assert res.errors[0].category == ErrorCategory.VALIDATION


def test_try_with_error_handler_throwing_preserves_handler_exception() -> None:
    def broken_handler(ex: Exception) -> Error:
        raise RuntimeError("Handler threw")

    res = try_(lambda: 10 // 0, error_handler=broken_handler)

    assert res.is_failure is True
    assert "RuntimeError: Handler threw" in res.errors[0].technical_message


def test_try_cancellation_maps_to_canceled_category() -> None:
    event = threading.Event()
    event.set()

    def canceled_action() -> int:
        raise asyncio.CancelledError()

    res = try_(canceled_action, cancellation_token=event)

    assert res.is_failure is True
    assert res.errors[0].category == ErrorCategory.CANCELED


def test_try_timeout_maps_to_unavailable_category() -> None:
    def timeout_action() -> int:
        raise TimeoutError("Socket timeout")

    res = try_(timeout_action)

    assert res.is_failure is True
    assert res.errors[0].category == ErrorCategory.UNAVAILABLE
    assert "timed out" in res.errors[0].user_message.lower()


def test_try_async_success_returns_ok() -> None:
    async def async_calc() -> int:
        await asyncio.sleep(0.001)
        return 42

    async def run() -> None:
        res = await try_async(async_calc)
        assert res.is_success is True
        assert res.value == 42

    asyncio.run(run())


def test_try_async_throws_returns_failure() -> None:
    async def async_fail() -> int:
        await asyncio.sleep(0.001)
        raise ConnectionResetError("Remote closed connection")

    async def run() -> None:
        res = await try_async(async_fail)
        assert res.is_failure is True
        assert "ConnectionResetError" in res.errors[0].technical_message

    asyncio.run(run())


def test_try_fatal_exception_bubbles_without_catching() -> None:
    def fatal_action() -> NoReturn:
        raise KeyboardInterrupt()

    with pytest.raises(KeyboardInterrupt):
        try_(fatal_action)
