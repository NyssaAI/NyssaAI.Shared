# Responsibility: Execute the observable email normalization, formatting, and rejection demo.
# Public interface:
# `-- demo()
"""Console demonstration of email parsing, interchangeable formatting, and expected failure."""

from addresses.email_address import to_email_address
from formatting.email_formatter import EmailFormatter
from formatting.email_formatter_json import email_formatter_json
from formatting.email_formatter_text import email_formatter_text


def demo() -> list[str]:
    """Run the valid and invalid email flows and return the observable output lines."""
    lines: list[str] = []
    parsed = to_email_address(" Ada@EXAMPLE.COM ")
    if not parsed.ok:
        raise RuntimeError(f"Expected valid demo email, got {parsed.error}")
    lines.append(f"normalized: {parsed.value}")
    formatters: tuple[EmailFormatter, ...] = (email_formatter_text(), email_formatter_json())
    for formatter in formatters:
        lines.append(formatter.format(parsed.value))
    rejected = to_email_address("not-an-email")
    if rejected.ok:
        raise RuntimeError("Expected invalid email to be rejected")
    lines.append(f"rejected: {rejected.error}")
    return lines


if __name__ == "__main__":
    for line in demo():
        print(line)
