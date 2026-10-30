# Responsibility: Validate and normalize an address into a distinct immutable value.
# Public interface:
# +-- EmailAddress
# +-- MAX_EMAIL_LENGTH
# `-- to_email_address(value)
"""Bounded ASCII email validation and domain-case normalization."""

import re
from typing import Final, NewType

from results.result import Result, fail, ok

EmailAddress = NewType("EmailAddress", str)

MAX_EMAIL_LENGTH: Final[int] = 254
_EMAIL_PATTERN: Final[re.Pattern[str]] = re.compile(
    r"^[A-Za-z0-9._%+-]+@[A-Za-z0-9-]+(?:\.[A-Za-z0-9-]+)+$"
)


def to_email_address(value: str) -> Result[EmailAddress, str]:
    """Validate an email string and normalize its domain part to lowercase."""
    candidate = value.strip()
    if len(candidate) > MAX_EMAIL_LENGTH or not _EMAIL_PATTERN.match(candidate):
        return fail("email.invalid")
    local_part, domain_part = candidate.split("@", 1)
    return ok(EmailAddress(f"{local_part}@{domain_part.lower()}"))
