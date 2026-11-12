# Responsibility: Mark keyring as a package and export OS, systemd-creds, file-backed, and in-memory master key custody types.
# Public interface:
# (none)
"""Keyring package."""

from nyssa_ai_configuration.keyring.keyring import (
    MANIFEST_KEY_ACCOUNT,
    Keyring,
    get_existing_key,
    get_or_create_key,
    resolve_keyring,
    resolve_manifest_keyring,
)
from nyssa_ai_configuration.keyring.keyring_default import KeyringDefault
from nyssa_ai_configuration.keyring.keyring_in_memory import KeyringInMemory
from nyssa_ai_configuration.keyring.keyring_protected_file import KeyringProtectedFile
from nyssa_ai_configuration.keyring.keyring_systemd_creds import KeyringSystemdCreds

__all__ = [
    "Keyring",
    "KeyringDefault",
    "KeyringInMemory",
    "KeyringProtectedFile",
    "KeyringSystemdCreds",
    "MANIFEST_KEY_ACCOUNT",
    "get_existing_key",
    "get_or_create_key",
    "resolve_keyring",
    "resolve_manifest_keyring",
]
