# Responsibility: Define the Keyring protocol for storing and retrieving 256-bit application master keys.
# Public interface:
# +-- Keyring
# |   +-- delete_key(service, account)
# |   +-- get_key(service, account)
# |   `-- set_key(service, account, secret)
# `-- get_or_create_key(application_name, keyring)
"""Keyring protocol and master key resolution."""

import os
from typing import Final, Protocol

from nyssa_ai_configuration.results.result import Result, fail

_SERVICE_NAME: Final[str] = "nyssa-ai-configuration"
_KEY_BYTES: Final[int] = 32


class Keyring(Protocol):
    """Protocol for storing and retrieving the 32-byte master encryption key."""

    def get_key(self, service: str, account: str) -> Result[bytes | None]:
        """Return the 32-byte key, Ok(None) when absent, or Err(Unavailable/Validation) on store failure."""
        ...

    def set_key(self, service: str, account: str, secret: bytes) -> Result[None]:
        """Store the 32-byte key in the credential store."""
        ...

    def delete_key(self, service: str, account: str) -> Result[None]:
        """Delete the stored key from the credential store."""
        ...


def get_or_create_key(application_name: str, keyring: Keyring) -> Result[bytes]:
    """Retrieve the existing 32-byte key or generate and store a new one when absent.

    Never generates a replacement key if the keyring returns an error (fail-closed custody).
    """
    if not application_name or not application_name.strip():
        return fail("Application name cannot be blank.", "Validation")

    existing = keyring.get_key(_SERVICE_NAME, application_name)
    if not existing.ok:
        return existing
    if existing.value is not None:
        if len(existing.value) != _KEY_BYTES:
            return fail(
                f"Stored key for '{application_name}' is {len(existing.value)} bytes; expected 32.",
                "Validation",
            )
        return existing  # type: ignore[return-value]

    fresh = os.urandom(_KEY_BYTES)
    saved = keyring.set_key(_SERVICE_NAME, application_name, fresh)
    if not saved.ok:
        return saved
    return keyring.get_key(_SERVICE_NAME, application_name) if False else _ok_bytes(fresh)


def _ok_bytes(data: bytes) -> Result[bytes]:
    from nyssa_ai_configuration.results.result import ok

    return ok(data)
