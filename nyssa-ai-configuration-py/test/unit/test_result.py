# Responsibility: Verify Result, Ok, Err, and ConfigurationError discriminated union invariants and narrowing.
# Public interface:
# +-- test_error_retains_cause_type_and_exposes_convenience_properties()
# +-- test_fail_creates_failed_result_with_category()
# `-- test_ok_creates_successful_result_with_value()
"""Unit tests for Result, Ok, Err, and ConfigurationError."""

from nyssa_ai_configuration.results.result import Err, Ok, Result, fail, ok


def test_ok_creates_successful_result_with_value() -> None:
    """Verify ok() constructs an Ok result and narrows cleanly via result.ok."""
    result: Result[int] = ok(42)

    assert result.ok is True
    assert isinstance(result, Ok)
    assert result.value == 42


def test_fail_creates_failed_result_with_category() -> None:
    """Verify fail() constructs an Err result with message and category."""
    result: Result[str] = fail("Key not found.", "NotFound")

    assert result.ok is False
    assert isinstance(result, Err)
    assert result.message == "Key not found."
    assert result.category == "NotFound"
    assert result.error.cause_type is None


def test_error_retains_cause_type_and_exposes_convenience_properties() -> None:
    """Verify Err exposes .message and .category directly and records the exception cause_type."""
    cause = ValueError("bad json")
    failed = fail("Invalid document.", "Validation", cause)

    assert not failed.ok
    assert failed.message == "Invalid document."
    assert failed.category == "Validation"
    assert failed.error.cause_type == "ValueError"
