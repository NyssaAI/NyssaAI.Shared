# Responsibility: Construct the provider for a human-readable email representation.
# Public interface:
# +-- EmailFormatterText
# |   `-- format(value)
# `-- email_formatter_text()
"""Plain-text representation provider for a validated EmailAddress."""

from dataclasses import dataclass

from addresses.email_address import EmailAddress
from formatting.email_formatter import EmailFormatter


@dataclass(frozen=True, slots=True)
class EmailFormatterText:
    """Stateless plain-text formatter prefixing the normalized address."""

    def format(self, value: EmailAddress) -> str:
        """Format the normalized email address with an explicit label."""
        return f"email: {value}"


def email_formatter_text() -> EmailFormatter:
    """Create a stateless plain-text email formatter."""
    return EmailFormatterText()
