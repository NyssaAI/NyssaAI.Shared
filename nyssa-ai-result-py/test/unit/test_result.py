"""Unit tests for Result core properties, state, and conversion methods."""

from __future__ import annotations

import asyncio
import pytest

from nyssa_ai_result.error import Error
from nyssa_ai_result.error_category import ErrorCategory
from nyssa_ai_result.result import Result, fail, ok
from nyssa_ai_result.result_exception import ResultException


def test_result_success_holds_value_and_empty_errors() -> None:
    res = ok(42)

    assert res.is_success is True
    assert res.is_failure is False
    assert res.ok is True
    assert bool(res) is True
    assert res.value == 42
    assert res.errors == ()


def test_result_failure_holds_errors() -> None:
    err = Error.from_message("Not found", ErrorCategory.NOT_FOUND)
    res: Result[int] = fail(err)

    assert res.is_success is False
    assert res.is_failure is True
    assert res.ok is False
    assert bool(res) is False
    assert res.errors == (err,)


def test_result_failure_from_string_constructs_error() -> None:
    res: Result[str] = fail("Invalid payload")

    assert res.is_failure is True
    assert len(res.errors) == 1
    assert res.errors[0].user_message == "Invalid payload"


def test_result_ok_and_fail_helpers() -> None:
    success = ok("hello")
    assert success.is_success is True
    assert success.value == "hello"

    failure = fail("something broke")
    assert failure.is_failure is True
    assert failure.errors[0].user_message == "something broke"


def test_result_throw_if_error_returns_value_or_raises_result_exception() -> None:
    success = ok("valid")
    assert success.throw_if_error() == "valid"

    failure: Result[str] = fail("failed operation")
    with pytest.raises(ResultException) as exc_info:
        failure.throw_if_error()

    assert "failed operation" in str(exc_info.value)
    assert len(exc_info.value.errors) == 1
    assert exc_info.value.errors[0].user_message == "failed operation"


def test_result_unwrap_returns_value_or_raises_result_exception() -> None:
    success = ok(100)
    assert success.unwrap() == 100

    failure: Result[int] = fail("cannot unwrap")
    with pytest.raises(ResultException):
        failure.unwrap()


def test_result_try_get_value_returns_tuple() -> None:
    success = ok("data")
    is_ok, val = success.try_get_value()
    assert is_ok is True
    assert val == "data"

    failure: Result[str] = fail("error")
    is_ok, val = failure.try_get_value()
    assert is_ok is False
    assert val is None


def test_result_resolve_executes_correct_branch() -> None:
    success = ok(10)
    resolved_success = success.resolve(
        on_success=lambda v: f"Value: {v}",
        on_failure=lambda errs: f"Error: {errs[0].user_message}",
    )
    assert resolved_success == "Value: 10"

    failure: Result[int] = fail("boom")
    resolved_failure = failure.resolve(
        on_success=lambda v: f"Value: {v}",
        on_failure=lambda errs: f"Error: {errs[0].user_message}",
    )
    assert resolved_failure == "Error: boom"


def test_result_resolve_async_executes_correct_branch() -> None:
    async def async_success(v: int) -> str:
        await asyncio.sleep(0.001)
        return f"Async: {v}"

    async def async_failure(errs: tuple[Error, ...]) -> str:
        await asyncio.sleep(0.001)
        return f"Async error: {errs[0].user_message}"

    async def run() -> None:
        res_ok = ok(5)
        out_ok = await res_ok.resolve_async(async_success, async_failure)
        assert out_ok == "Async: 5"

        res_fail: Result[int] = fail("failed")
        out_fail = await res_fail.resolve_async(async_success, async_failure)
        assert out_fail == "Async error: failed"

    asyncio.run(run())
