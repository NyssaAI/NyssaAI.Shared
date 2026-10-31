# Responsibility: Represent an opaque, authenticated recovery token with redacted string formatting.
# Public interface:
# `-- ConfigurationRecoveryHandle
#     +-- export()
#     +-- import_token(token)
#     `-- new(token)
"""Opaque recovery handle wrapper preventing accidental token leakage in logs."""

from nyssa_ai_configuration.results.result import Result, fail, ok


class ConfigurationRecoveryHandle:
    """An opaque, authenticated recovery token persisted only through explicit export()."""

    def __init__(self, token: str) -> None:
        """Initialize with a raw encrypted token string."""
        self._token = token

    @classmethod
    def import_token(cls, token: str) -> Result["ConfigurationRecoveryHandle"]:
        """Import a previously exported token string for validation during recover()."""
        if not token or not token.strip():
            return fail("Recovery handle token cannot be blank.", "Validation")
        return ok(cls(token.strip()))

    def export(self) -> str:
        """Export the protected token string for caller-owned persistence."""
        return self._token

    def __str__(self) -> str:
        """Return a redacted description so the token is never written to logs."""
        return "ConfigurationRecoveryHandle { protected }"

    def __repr__(self) -> str:
        """Return a redacted representation so the token is never written to logs."""
        return "ConfigurationRecoveryHandle { protected }"

    def __eq__(self, other: object) -> bool:
        """Compare two handles by their underlying token."""
        return isinstance(other, ConfigurationRecoveryHandle) and self._token == other._token
