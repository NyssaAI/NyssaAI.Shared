"""Unit tests for Guard input validation methods."""

from __future__ import annotations

import pytest

from nyssa_ai_result.error_category import ErrorCategory
from nyssa_ai_result.guard import Guard


def test_guard_none_validates_presence() -> None:
    ok_res = Guard.against.none("hello", parameter_name="greeting")
    assert ok_res.is_success is True
    assert ok_res.value == "hello"

    fail_res = Guard.against.none(None, parameter_name="greeting")
    assert fail_res.is_failure is True
    assert fail_res.errors[0].category == ErrorCategory.VALIDATION
    assert "greeting cannot be None" in fail_res.errors[0].user_message


def test_guard_null_alias_validates_presence() -> None:
    ok_res = Guard.against.null(123)
    assert ok_res.is_success is True
    assert ok_res.value == 123

    fail_res = Guard.against.null(None)
    assert fail_res.is_failure is True


def test_guard_null_or_empty_validates_string_and_sequence() -> None:
    assert Guard.against.null_or_empty("valid").is_success is True
    assert Guard.against.null_or_empty("").is_failure is True
    assert Guard.against.null_or_empty(None).is_failure is True

    assert Guard.against.null_or_empty([1, 2, 3]).is_success is True
    assert Guard.against.null_or_empty([]).is_failure is True


def test_guard_null_or_whitespace_validates_non_blank() -> None:
    assert Guard.against.null_or_whitespace("  hello  ").is_success is True
    assert Guard.against.null_or_whitespace("   \t\n  ").is_failure is True
    assert Guard.against.null_or_whitespace(None).is_failure is True


def test_guard_out_of_range_validates_inclusive_bounds() -> None:
    assert Guard.against.out_of_range(5, 1, 10).is_success is True
    assert Guard.against.out_of_range(1, 1, 10).is_success is True
    assert Guard.against.out_of_range(10, 1, 10).is_success is True

    fail_low = Guard.against.out_of_range(0, 1, 10)
    assert fail_low.is_failure is True

    fail_high = Guard.against.out_of_range(11, 1, 10)
    assert fail_high.is_failure is True


def test_guard_less_than_and_greater_than_validate_inequalities() -> None:
    # less_than rejects value < min_val (requires value >= min_val)
    assert Guard.against.less_than(5, 5).is_success is True
    assert Guard.against.less_than(4, 5).is_failure is True

    # less_than_or_equal rejects value <= min_val (requires value > min_val)
    assert Guard.against.less_than_or_equal(6, 5).is_success is True
    assert Guard.against.less_than_or_equal(5, 5).is_failure is True

    # greater_than rejects value > max_val (requires value <= max_val)
    assert Guard.against.greater_than(5, 5).is_success is True
    assert Guard.against.greater_than(6, 5).is_failure is True

    # greater_than_or_equal rejects value >= max_val (requires value < max_val)
    assert Guard.against.greater_than_or_equal(4, 5).is_success is True
    assert Guard.against.greater_than_or_equal(5, 5).is_failure is True


def test_guard_length_out_of_range_validates_character_count() -> None:
    assert Guard.against.length_out_of_range("hello", 3, 10).is_success is True
    assert Guard.against.length_out_of_range("hi", 3, 10).is_failure is True
    assert Guard.against.length_out_of_range("superlongstring", 3, 10).is_failure is True
    assert Guard.against.length_out_of_range("abc", 5, 2).is_failure is True


def test_guard_pattern_mismatch_validates_regex_match() -> None:
    email_pattern = r"^[^@]+@[^@]+\.[^@]+$"
    assert Guard.against.pattern_mismatch("user@example.com", email_pattern).is_success is True
    assert Guard.against.pattern_mismatch("invalid-email", email_pattern).is_failure is True
    assert Guard.against.pattern_mismatch(None, email_pattern).is_failure is True


def test_guard_true_and_false_validate_boolean_and_predicates() -> None:
    # true rejects True condition
    assert Guard.against.true(False, "Condition was true").is_success is True
    assert Guard.against.true(True, "Condition was true").is_failure is True

    # false rejects False condition
    assert Guard.against.false(True, "Condition was false").is_success is True
    assert Guard.against.false(False, "Condition was false").is_failure is True

    # predicate checks
    assert Guard.against.true(10, lambda x: x < 0, "Number cannot be negative").is_success is True
    assert Guard.against.true(-1, lambda x: x < 0, "Number cannot be negative").is_failure is True

    assert Guard.against.false(10, lambda x: x > 0, "Number must be positive").is_success is True
    assert Guard.against.false(-1, lambda x: x > 0, "Number must be positive").is_failure is True


def test_guard_contains_and_not_contains_validate_elements() -> None:
    items = [1, 2, 3, 4]

    # contains rejects if any element matches predicate
    assert Guard.against.contains(items, lambda x: x > 10, "Contains element > 10").is_success is True
    assert Guard.against.contains(items, lambda x: x == 3, "Contains 3").is_failure is True

    # not_contains rejects if no element matches predicate
    assert Guard.against.not_contains(items, lambda x: x == 2, "Does not contain 2").is_success is True
    assert Guard.against.not_contains(items, lambda x: x == 99, "Does not contain 99").is_failure is True
