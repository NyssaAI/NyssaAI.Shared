# Responsibility: Construct the provider for a JSON email representation.
# Public interface:
# +-- EmailFormatterJson
# |   `-- format(value)
# `-- email_formatter_json()
"""JSON representation provider for a validated EmailAddress."""

import json
from dataclasses import dataclass

from addresses.email_address import EmailAddress
from formatting.email_formatter import EmailFormatter


@dataclass(frozen=True, slots=True)
class EmailFormatterJson:
    """Stateless JSON formatter emitting a compact single-field object."""

    def format(self, value: EmailAddress) -> str:
        """Serialize the normalized email address as a compact JSON object."""
        return json.dumps({"email": str(value)}, separators=(",", ":"))


def email_formatter_json() -> EmailFormatter:
    """Create a stateless JSON email formatter."""
    return EmailFormatterJson()
