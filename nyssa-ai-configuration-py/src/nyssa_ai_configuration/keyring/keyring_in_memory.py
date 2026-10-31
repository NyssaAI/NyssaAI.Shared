# Responsibility: Store 256-bit configuration master keys in process memory for deterministic testing.
# Public interface:
# `-- KeyringInMemory
#     +-- delete_key(service, account)
#     +-- get_key(service, account)
#     +-- new(initial_keys, fail_on_read, fail_on_write)
#     `-- set_key(service, account, secret)
"""In-memory Keyring implementation."""

from collections.abc import Mapping

from nyssa_ai_configuration.results.result import Result, fail, ok


class KeyringInMemory:
    """Process-memory keyring for deterministic unit and use-case tests."""

    def __init__(
        self,
        initial_keys: Mapping[str, bytes] | None = None,
        fail_on_read: bool = False,
        fail_on_write: bool = False,
    ) -> None:
        """Initialize with optional pre-populated keys and fault-injection flags."""
        self._entries: dict[str, bytes] = dict(initial_keys) if initial_keys is not None else {}
        self._fail_on_read = fail_on_read
        self._fail_on_write = fail_on_write

    def get_key(self, service: str, account: str) -> Result[bytes | None]:
        """Return the stored 32-byte key or Ok(None) if not present."""
        if self._fail_on_read:
            return fail("Keyring read failure simulated.", "Unavailable")
        found = self._entries.get(f"{service}/{account}")
        if found is None:
            return ok(None)
        if len(found) != 32:
            return fail(f"Stored key is {len(found)} bytes; expected 32.", "Validation")
        return ok(bytes(found))

    def set_key(self, service: str, account: str, secret: bytes) -> Result[None]:
        """Store a 32-byte key in memory."""
        if self._fail_on_write:
            return fail("Keyring write failure simulated.", "Unavailable")
        if len(secret) != 32:
            return fail(f"Key must be 32 bytes; {len(secret)} supplied.", "Validation")
        self._entries[f"{service}/{account}"] = bytes(secret)
        return ok(None)

    def delete_key(self, service: str, account: str) -> Result[None]:
        """Delete a stored key from memory."""
        self._entries.pop(f"{service}/{account}", None)
        return ok(None)
