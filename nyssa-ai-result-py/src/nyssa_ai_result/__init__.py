# Responsibility: Export the public surface of nyssa_ai_result.
# Public interface:
# (none)
"""Railway-oriented programming Result pattern for Python."""

from nyssa_ai_result.error import Error
from nyssa_ai_result.error_category import ErrorCategory
from nyssa_ai_result.guard import Guard
from nyssa_ai_result.result import Err, Ok, Result, fail, fail_error, ok, try_, try_async
from nyssa_ai_result.result_exception import ResultException

__all__ = [
    "Err",
    "Error",
    "ErrorCategory",
    "Guard",
    "Ok",
    "Result",
    "ResultException",
    "fail",
    "fail_error",
    "ok",
    "try_",
    "try_async",
]
