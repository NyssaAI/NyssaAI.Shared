"""Unit tests for Result monadic chaining and pure combinator semantics."""

from __future__ import annotations

import asyncio
import pytest

from nyssa_ai_result.error import Error
from nyssa_ai_result.result import Result, fail, ok


def test_combinator_then_on_success_chains_next_operation() -> None:
    res = ok(5).then(lambda v: ok(v * 2))
    assert res.is_success is True
    assert res.value == 10


def test_combinator_then_on_failure_short_circuits_and_propagates() -> None:
    called = False

    def next_op(v: int) -> Result[int]:
        nonlocal called
        called = True
        return ok(v * 2)

    failure: Result[int] = fail("Initial failure")
    res = failure.then(next_op)

    assert called is False
    assert res.is_failure is True
    assert res.errors == failure.errors


def test_combinator_then_on_programmer_defect_bubbles_exception_immediately() -> None:
    def buggy_delegate(v: int) -> Result[int]:
        raise ZeroDivisionError("Defect: divided by zero")

    with pytest.raises(ZeroDivisionError) as exc_info:
        ok(10).then(buggy_delegate)

    assert "Defect: divided by zero" in str(exc_info.value)


def test_combinator_then_with_none_delegate_raises_value_error() -> None:
    with pytest.raises(ValueError):
        ok(10).then(None)  # type: ignore[arg-type]


def test_combinator_then_async_chains_coroutine() -> None:
    async def async_double(v: int) -> Result[int]:
        await asyncio.sleep(0.001)
        return ok(v * 2)

    async def run() -> None:
        res = await ok(7).then_async(async_double)
        assert res.is_success is True
        assert res.value == 14

    asyncio.run(run())


def test_combinator_then_async_on_programmer_defect_bubbles_exception() -> None:
    async def buggy_async(v: int) -> Result[int]:
        await asyncio.sleep(0.001)
        raise IndexError("Defect: index out of bounds")

    async def run() -> None:
        with pytest.raises(IndexError):
            await ok(42).then_async(buggy_async)

    asyncio.run(run())


def test_combinator_transform_projects_value() -> None:
    res = ok(10).transform(lambda v: f"Value is {v}")

    assert res.is_success is True
    assert res.value == "Value is 10"


def test_combinator_transform_on_failure_propagates_errors() -> None:
    called = False

    def transform_fn(v: int) -> str:
        nonlocal called
        called = True
        return str(v)

    failure: Result[int] = fail("Failed upstream")
    res = failure.transform(transform_fn)

    assert called is False
    assert res.is_failure is True
    assert res.errors == failure.errors


def test_combinator_transform_on_programmer_defect_bubbles_exception() -> None:
    def buggy_transform(v: int) -> str:
        raise KeyError("Defect: missing key")

    with pytest.raises(KeyError) as exc_info:
        ok(5).transform(buggy_transform)

    assert "Defect: missing key" in str(exc_info.value)


def test_combinator_transform_async_projects_value() -> None:
    async def async_transform(v: int) -> str:
        await asyncio.sleep(0.001)
        return f"Async {v}"

    async def run() -> None:
        res = await ok(12).transform_async(async_transform)
        assert res.is_success is True
        assert res.value == "Async 12"

    asyncio.run(run())


def test_combinator_transform_async_on_programmer_defect_bubbles_exception() -> None:
    async def buggy_transform_async(v: int) -> str:
        await asyncio.sleep(0.001)
        raise TypeError("Defect: unsupported operand")

    async def run() -> None:
        with pytest.raises(TypeError):
            await ok(8).transform_async(buggy_transform_async)

    asyncio.run(run())
