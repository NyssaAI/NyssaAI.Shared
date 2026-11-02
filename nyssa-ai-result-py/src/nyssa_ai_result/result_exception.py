# Responsibility: Carry structured errors from an explicitly thrown failed result.
# Public interface:
# `-- ResultException
"""Exception raised when a failed Result is unwrapped or explicitly thrown."""

from __future__ import annotations

from typing import TYPE_CHECKING

if TYPE_CHECKING:
    from nyssa_ai_result.error import Error


def _init_result_exception(self: Exception, errors: tuple[Error, ...]) -> None:
    setattr(self, "errors", errors)
    messages = [e.user_message for e in errors]
    Exception.__init__(self, "\n".join(messages) if messages else "Result failure")


ResultException: type[Exception] = type(
    "ResultException",
    (Exception,),
    {
        "__doc__": "Carries structured errors from an explicitly thrown or unwrapped failed result.",
        "__init__": _init_result_exception,
        "errors": (),
    },
)
