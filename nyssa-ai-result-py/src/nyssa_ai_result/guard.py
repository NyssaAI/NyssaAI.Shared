# Responsibility: Provide input validation guards returning Result outcomes.
# Public interface:
# `-- Guard
#     +-- against
#     +-- contains(collection, predicate, message, technical_message, category)
#     +-- false(condition_or_value, predicate_or_message, message, technical_message, category)
#     +-- greater_than(value, max_val, parameter_name)
#     +-- greater_than_or_equal(value, max_val, parameter_name)
#     +-- length_out_of_range(value, min_length, max_length, parameter_name)
#     +-- less_than(value, min_val, parameter_name)
#     +-- less_than_or_equal(value, min_val, parameter_name)
#     +-- none(value, parameter_name)
#     +-- not_contains(collection, predicate, message, technical_message, category)
#     +-- null(value, parameter_name)
#     +-- null_or_empty(value, parameter_name)
#     +-- null_or_whitespace(value, parameter_name)
#     +-- out_of_range(value, min_val, max_val, parameter_name)
#     +-- pattern_mismatch(value, pattern, parameter_name)
#     `-- true(condition_or_value, predicate_or_message, message, technical_message, category)
"""Validation guards returning railway-oriented Result outcomes."""

from __future__ import annotations

import re
from collections.abc import Callable, Iterable, Sequence
from typing import TypeVar

from nyssa_ai_result.error import Error
from nyssa_ai_result.error_category import ErrorCategory
from nyssa_ai_result.result import Result, fail, ok

T = TypeVar("T")


def _invalid(user_msg: str, tech_msg: str) -> Error:
    return Error.from_messages(user_msg, tech_msg, ErrorCategory.VALIDATION)


class Guard:
    """Precondition and invariant validation guards returning Result outcomes."""

    against: type[Guard]

    @staticmethod
    def none(value: T | None, parameter_name: str = "value") -> Result[T]:
        """Reject None, returning the validated value on success."""
        if value is None:
            return fail(_invalid(f"{parameter_name} cannot be None", f"{parameter_name} is None"))
        return ok(value)

    @staticmethod
    def null(value: T | None, parameter_name: str = "value") -> Result[T]:
        """Reject None (alias for none)."""
        return Guard.none(value, parameter_name)

    @staticmethod
    def null_or_empty(
        value: str | Sequence[object] | None,
        parameter_name: str = "value",
    ) -> Result[object]:
        """Reject None or empty string/collection."""
        if value is None:
            return fail(_invalid(f"{parameter_name} cannot be empty", f"{parameter_name} is None"))
        if len(value) == 0:
            return fail(_invalid(f"{parameter_name} cannot be empty", f"{parameter_name} is empty"))
        return ok(value)

    @staticmethod
    def null_or_whitespace(value: str | None, parameter_name: str = "value") -> Result[str]:
        """Reject None, empty, or whitespace-only string."""
        if value is None:
            return fail(_invalid(f"{parameter_name} cannot be empty", f"{parameter_name} is None"))
        if not value.strip():
            return fail(_invalid(f"{parameter_name} cannot be empty", f"{parameter_name} is whitespace"))
        return ok(value)

    @staticmethod
    def out_of_range(value: T, min_val: T, max_val: T, parameter_name: str = "value") -> Result[T]:
        """Validate value is within inclusive range [min_val, max_val]."""
        if value is None:
            return fail(_invalid(f"{parameter_name} cannot be None", f"{parameter_name} is None"))
        if value < min_val or value > max_val:  # type: ignore[operator]
            return fail(
                _invalid(
                    f"{parameter_name} must be between {min_val} and {max_val}",
                    f"{parameter_name} value {value} is outside range [{min_val}, {max_val}]",
                )
            )
        return ok(value)

    @staticmethod
    def less_than(value: T, min_val: T, parameter_name: str = "value") -> Result[T]:
        """Reject value strictly less than min_val (requires value >= min_val)."""
        if value is None:
            return fail(_invalid(f"{parameter_name} cannot be None", f"{parameter_name} is None"))
        if value < min_val:  # type: ignore[operator]
            return fail(
                _invalid(
                    f"{parameter_name} must be greater than or equal to {min_val}",
                    f"{parameter_name} value {value} is less than {min_val}",
                )
            )
        return ok(value)

    @staticmethod
    def less_than_or_equal(value: T, min_val: T, parameter_name: str = "value") -> Result[T]:
        """Reject value less than or equal to min_val (requires value > min_val)."""
        if value is None:
            return fail(_invalid(f"{parameter_name} cannot be None", f"{parameter_name} is None"))
        if value <= min_val:  # type: ignore[operator]
            return fail(
                _invalid(
                    f"{parameter_name} must be greater than {min_val}",
                    f"{parameter_name} value {value} is not greater than {min_val}",
                )
            )
        return ok(value)

    @staticmethod
    def greater_than(value: T, max_val: T, parameter_name: str = "value") -> Result[T]:
        """Reject value strictly greater than max_val (requires value <= max_val)."""
        if value is None:
            return fail(_invalid(f"{parameter_name} cannot be None", f"{parameter_name} is None"))
        if value > max_val:  # type: ignore[operator]
            return fail(
                _invalid(
                    f"{parameter_name} must be less than or equal to {max_val}",
                    f"{parameter_name} value {value} exceeds {max_val}",
                )
            )
        return ok(value)

    @staticmethod
    def greater_than_or_equal(value: T, max_val: T, parameter_name: str = "value") -> Result[T]:
        """Reject value greater than or equal to max_val (requires value < max_val)."""
        if value is None:
            return fail(_invalid(f"{parameter_name} cannot be None", f"{parameter_name} is None"))
        if value >= max_val:  # type: ignore[operator]
            return fail(
                _invalid(
                    f"{parameter_name} must be less than {max_val}",
                    f"{parameter_name} value {value} is not less than {max_val}",
                )
            )
        return ok(value)

    @staticmethod
    def length_out_of_range(
        value: str | None,
        min_length: int,
        max_length: int,
        parameter_name: str = "value",
    ) -> Result[str]:
        """Reject None, invalid bounds, or string length outside [min_length, max_length]."""
        if value is None:
            return fail(_invalid(f"{parameter_name} cannot be None", f"{parameter_name} is None"))
        if min_length < 0 or max_length < min_length:
            return fail(
                _invalid(
                    f"{parameter_name} length bounds are invalid",
                    "Length bounds must be nonnegative and ordered",
                )
            )
        if len(value) < min_length or len(value) > max_length:
            return fail(
                _invalid(
                    f"{parameter_name} must be between {min_length} and {max_length} characters",
                    f"{parameter_name} length {len(value)} is outside range [{min_length}, {max_length}]",
                )
            )
        return ok(value)

    @staticmethod
    def pattern_mismatch(value: str | None, pattern: str, parameter_name: str = "value") -> Result[str]:
        """Reject None or string not matching regex pattern."""
        if value is None:
            return fail(_invalid(f"{parameter_name} format is invalid", f"{parameter_name} is None"))
        try:
            matched = bool(re.search(pattern, value))
        except re.error as exc:
            return fail(
                _invalid(
                    f"{parameter_name} could not be checked.",
                    f"{parameter_name} pattern '{pattern}' is not valid: {exc}",
                )
            )
        if not matched:
            return fail(
                _invalid(
                    f"{parameter_name} format is invalid",
                    f"{parameter_name} value '{value}' does not match pattern '{pattern}'",
                )
            )
        return ok(value)

    @staticmethod
    def true(
        condition_or_value: object,
        predicate_or_message: Callable[[object], bool] | str,
        message: str | None = None,
        technical_message: str | None = None,
        category: ErrorCategory = ErrorCategory.VALIDATION,
    ) -> Result[object]:
        """Reject a true condition or a value whose predicate is true."""
        if callable(predicate_or_message):
            val = condition_or_value
            pred = predicate_or_message
            msg = message if message is not None else "Condition is invalid"
            tech = technical_message if technical_message is not None else msg
            try:
                verdict = pred(val)
            except Exception as exc:
                return fail(Error.from_caught(exc)[0])
            if verdict:
                return fail(Error.from_messages(msg, tech, category))
            return ok(val)
        is_bad = bool(condition_or_value)
        msg_str = str(predicate_or_message)
        tech_str = technical_message if technical_message is not None else msg_str
        if is_bad:
            return fail(Error.from_messages(msg_str, tech_str, category))
        return ok(None)

    @staticmethod
    def false(
        condition_or_value: object,
        predicate_or_message: Callable[[object], bool] | str,
        message: str | None = None,
        technical_message: str | None = None,
        category: ErrorCategory = ErrorCategory.VALIDATION,
    ) -> Result[object]:
        """Reject a false condition or a value whose predicate is false."""
        if callable(predicate_or_message):
            val = condition_or_value
            pred = predicate_or_message
            msg = message if message is not None else "Condition is invalid"
            tech = technical_message if technical_message is not None else msg
            try:
                verdict = pred(val)
            except Exception as exc:
                return fail(Error.from_caught(exc)[0])
            if not verdict:
                return fail(Error.from_messages(msg, tech, category))
            return ok(val)
        is_good = bool(condition_or_value)
        msg_str = str(predicate_or_message)
        tech_str = technical_message if technical_message is not None else msg_str
        if not is_good:
            return fail(Error.from_messages(msg_str, tech_str, category))
        return ok(None)

    @staticmethod
    def contains(
        collection: Iterable[T] | None,
        predicate: Callable[[T], bool] | None,
        message: str,
        technical_message: str | None = None,
        category: ErrorCategory = ErrorCategory.VALIDATION,
    ) -> Result[Iterable[T]]:
        """Reject collection containing any item matching predicate."""
        if collection is None:
            return fail(_invalid(message, "The collection cannot be None."))
        if predicate is None:
            return fail(_invalid(message, "The predicate cannot be None."))
        try:
            for item in collection:
                if predicate(item):
                    return fail(Error.from_messages(message, technical_message or message, category))
        except Exception as exc:
            return fail(Error.from_caught(exc)[0])
        return ok(collection)

    @staticmethod
    def not_contains(
        collection: Iterable[T] | None,
        predicate: Callable[[T], bool] | None,
        message: str,
        technical_message: str | None = None,
        category: ErrorCategory = ErrorCategory.VALIDATION,
    ) -> Result[Iterable[T]]:
        """Reject collection with no item matching predicate."""
        if collection is None:
            return fail(_invalid(message, "The collection cannot be None."))
        if predicate is None:
            return fail(_invalid(message, "The predicate cannot be None."))
        try:
            for item in collection:
                if predicate(item):
                    return ok(collection)
        except Exception as exc:
            return fail(Error.from_caught(exc)[0])
        return fail(Error.from_messages(message, technical_message or message, category))


setattr(Guard, "against", Guard)
