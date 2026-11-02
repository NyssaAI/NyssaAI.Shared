# Responsibility: Define categories classifying failure outcomes for railway-oriented results.
# Public interface:
# `-- ErrorCategory
#     +-- CANCELED
#     +-- CONFLICT
#     +-- FORBIDDEN
#     +-- INDETERMINATE
#     +-- NOT_FOUND
#     +-- UNAVAILABLE
#     +-- UNEXPECTED
#     `-- VALIDATION
"""Failure categories for structured errors."""

from enum import StrEnum


class ErrorCategory(StrEnum):
    """Categorizes the reason for a result failure."""

    UNEXPECTED = "Unexpected"
    VALIDATION = "Validation"
    NOT_FOUND = "NotFound"
    CONFLICT = "Conflict"
    FORBIDDEN = "Forbidden"
    UNAVAILABLE = "Unavailable"
    CANCELED = "Canceled"
    INDETERMINATE = "Indeterminate"
