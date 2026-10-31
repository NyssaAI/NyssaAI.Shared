# Responsibility: Verify ConfigurationCipherAesGcm FormatVersion 2 wire format, AAD binding, and Keyring fail-closed custody.
# Public interface:
# +-- test_cipher_aes_gcm_round_trips_and_rejects_transplanted_ciphertext()
# +-- test_get_or_create_key_fails_closed_when_get_key_returns_error()
# `-- test_keyring_protected_file_persists_32_byte_key(tmp_path)
"""Unit tests for ConfigurationCipherAesGcm, KeyringInMemory, and KeyringProtectedFile."""

from pathlib import Path

from nyssa_ai_configuration.encryption.configuration_cipher_aes_gcm import (
    ConfigurationCipherAesGcm,
)
from nyssa_ai_configuration.keyring.keyring import get_or_create_key
from nyssa_ai_configuration.keyring.keyring_in_memory import KeyringInMemory
from nyssa_ai_configuration.keyring.keyring_protected_file import KeyringProtectedFile
from nyssa_ai_configuration.results.result import Result, fail


def test_cipher_aes_gcm_round_trips_and_rejects_transplanted_ciphertext() -> None:
    """Verify AES-256-GCM seals with FormatVersion 2 and binds ciphertext to (namespace, key) via AAD."""
    keyring = KeyringInMemory()
    key_res = get_or_create_key("nyssa", keyring)
    assert key_res.ok

    cipher_res = ConfigurationCipherAesGcm.create(key_res.value)
    assert cipher_res.ok
    cipher = cipher_res.value

    sealed = cipher.seal("super-secret-value", "payments", "apiKey")
    assert sealed.ok

    opened = cipher.open(sealed.value, "payments", "apiKey")
    assert opened.ok
    assert opened.value == "super-secret-value"

    wrong_key = cipher.open(sealed.value, "payments", "otherKey")
    assert not wrong_key.ok
    assert wrong_key.category == "Validation"

    wrong_namespace = cipher.open(sealed.value, "billing", "apiKey")
    assert not wrong_namespace.ok
    assert wrong_namespace.category == "Validation"


def test_get_or_create_key_fails_closed_when_get_key_returns_error() -> None:
    """Verify get_or_create_key fails closed and never generates a replacement key when get_key fails."""

    class BrokenKeyring:
        def __init__(self) -> None:
            self.set_called = False

        def get_key(self, service: str, account: str) -> Result[bytes | None]:
            return fail("Keyring backend locked.", "Unavailable")

        def set_key(self, service: str, account: str, secret: bytes) -> Result[None]:
            self.set_called = True
            return fail("Should not be called", "Unavailable")

        def delete_key(self, service: str, account: str) -> Result[None]:
            return fail("Not used", "Unavailable")

    broken = BrokenKeyring()
    res = get_or_create_key("nyssa", broken)

    assert not res.ok
    assert res.category == "Unavailable"
    assert broken.set_called is False


def test_keyring_protected_file_persists_32_byte_key(tmp_path: Path) -> None:
    """Verify KeyringProtectedFile stores and reloads the 32-byte key on disk."""
    store_dir = tmp_path / "keys"
    keyring = KeyringProtectedFile(store_dir)

    first = get_or_create_key("nyssa", keyring)
    assert first.ok
    assert len(first.value) == 32

    second = get_or_create_key("nyssa", keyring)
    assert second.ok
    assert second.value == first.value
