# Responsibility: Define the Keyring protocol for storing and retrieving 256-bit application master keys.
# Public interface:
# +-- Keyring
# |   +-- delete_key(service, account)
# |   +-- get_key(service, account)
# |   `-- set_key(service, account, secret)
# +-- get_or_create_key(application_name, keyring)
# `-- resolve_keyring(configuration_directory, options, keyring)
"""Keyring protocol and master key resolution."""

import os
import sys
from pathlib import Path
from typing import Final, Protocol

from nyssa_ai_configuration.manifest.types import BootstrapOptions
from nyssa_ai_result import Result, fail

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


def resolve_keyring(
    configuration_directory: str | Path,
    options: BootstrapOptions | None = None,
    keyring: Keyring | None = None,
) -> Keyring:
    """Resolve the active keyring strategy when no explicit keyring is provided.

    1. Systemd Headless Server (CREDENTIALS_DIRECTORY) -> KeyringSystemdCreds
    2. Protected File Fallback (.nyssa-key, NYSSA_CONFIG_DIR, or container/headless Linux) -> KeyringProtectedFile
    3. Desktop Workstation -> KeyringDefault
    """
    if keyring is not None:
        return keyring

    from nyssa_ai_configuration.keyring.keyring_default import KeyringDefault
    from nyssa_ai_configuration.keyring.keyring_protected_file import (
        _KEY_FILENAME,
        KeyringProtectedFile,
    )
    from nyssa_ai_configuration.keyring.keyring_systemd_creds import KeyringSystemdCreds

    if KeyringSystemdCreds.is_available():
        return KeyringSystemdCreds(
            options.systemd_credential_name if options is not None else None
        )

    config_dir = Path(str(configuration_directory)).resolve()
    if (config_dir / _KEY_FILENAME).exists():
        return KeyringProtectedFile(config_dir)

    env_config_dir = os.environ.get("NYSSA_CONFIG_DIR")
    if env_config_dir and env_config_dir.strip():
        return KeyringProtectedFile(config_dir)

    if Path("/.dockerenv").exists() or Path("/run/.containerenv").exists():
        return KeyringProtectedFile(config_dir)

    if (
        sys.platform.startswith("linux")
        and not os.environ.get("DBUS_SESSION_BUS_ADDRESS")
        and not os.environ.get("DISPLAY")
        and not os.environ.get("WAYLAND_DISPLAY")
    ):
        return KeyringProtectedFile(config_dir)

    return KeyringDefault()


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
        return _ok_bytes(existing.value)

    fresh = os.urandom(_KEY_BYTES)
    saved = keyring.set_key(_SERVICE_NAME, application_name, fresh)
    if not saved.ok:
        return saved
    return _ok_bytes(fresh)


def _ok_bytes(data: bytes) -> Result[bytes]:
    from nyssa_ai_result import ok

    return ok(data)
