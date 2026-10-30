# Responsibility: Register a validated email through a persistence store and own its correlated operational outcome.
# Public interface:
# `-- register_email(store, logger, email, correlation_id, monotonic_now)
"""Email registration workflow owning validation, persistence translation, and boundary logging."""

import asyncio
import re
import time
from collections.abc import Callable
from typing import Final

from addresses.email_address import EmailAddress, to_email_address
from persistence.registration_log import RegistrationLog
from persistence.types import (
    RegistrationCode,
    RegistrationFailure,
    RegistrationOutcome,
    RegistrationResult,
    RegistrationSuccess,
)

_CORRELATION_PATTERN: Final[re.Pattern[str]] = re.compile(r"^[A-Za-z0-9_-]{1,64}$")
_SLOW_THRESHOLD_MS: Final[float] = 250.0


def register_email(
    store: Callable[[EmailAddress], bool],
    logger: RegistrationLog,
    email: str,
    correlation_id: str,
    monotonic_now: Callable[[], float] = lambda: time.monotonic() * 1000.0,
) -> RegistrationResult:
    """Validate and register an email once while recording one correlated terminal outcome.

    The store callable returns True when inserted and False when the normalized address already exists.
    Caller cancellation (asyncio.CancelledError) propagates unchanged after recording cancellation.
    """
    if not _CORRELATION_PATTERN.match(correlation_id):
        raise ValueError("Invalid correlation ID")
    started = monotonic_now()
    outcome: RegistrationOutcome = "failure"
    code: RegistrationCode = "database.unavailable"
    used_database = False
    cancelled = False
    try:
        parsed = to_email_address(email)
        if not parsed.ok:
            outcome = "rejected"
            code = "email.invalid"
            return RegistrationFailure(error="email.invalid")
        used_database = True
        try:
            inserted = store(parsed.value)
        except asyncio.CancelledError:
            cancelled = True
            raise
        except Exception as cause:
            outcome = "failure"
            code = "database.unavailable"
            return RegistrationFailure(error="database.unavailable", cause=cause)
        if not inserted:
            outcome = "rejected"
            code = "email.exists"
            return RegistrationFailure(error="email.exists")
        outcome = "success"
        code = "none"
        return RegistrationSuccess()
    finally:
        if cancelled:
            outcome = "cancelled"
            code = "caller.cancelled"
        elapsed_ms = max(0.0, monotonic_now() - started)
        if used_database and elapsed_ms > _SLOW_THRESHOLD_MS:
            logger.slow(correlation_id, elapsed_ms)
        logger.completed(correlation_id, outcome, elapsed_ms, code, 1 if used_database else 0)
