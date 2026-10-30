# Responsibility: Define the format-provider protocol consumed by the example workflow.
# Public interface:
# `-- EmailFormatter
#     `-- format(value)
"""Substitutable formatting contract for validated email addresses."""

from typing import Protocol

from addresses.email_address import EmailAddress


class EmailFormatter(Protocol):
    """Render a validated EmailAddress without mutation or I/O."""

    def format(self, value: EmailAddress) -> str:
        """Return the provider-specific string representation."""
        ...
