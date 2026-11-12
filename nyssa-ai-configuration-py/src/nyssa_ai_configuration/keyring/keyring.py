# Responsibility: Define the Keyring protocol for storing and retrieving 256-bit application master keys.
# Public interface:
# +-- Keyring
# |   +-- delete_key(service, account)
# |   +-- get_key(service, account)
# |   `-- set_key(service, account, secret)
# +-- MANIFEST_KEY_ACCOUNT
# +-- get_existing_key(account_name, keyring)
# +-- get_or_create_key(application_name, keyring)
# +-- resolve_keyring(configuration_directory, options, keyring, manifest_directory)
# `-- resolve_manifest_keyring(manifest_path, keyring)
"""Keyring protocol and master key resolution."""

import os
from pathlib import Path
import re
import sys
from typing import Final, Protocol

from nyssa_ai_configuration.manifest.types import BootstrapOptions
from nyssa_ai_result import Result, fail, ok

MANIFEST_KEY_ACCOUNT: Final[str] = "_manifest"
_SERVICE_NAME: Final[str] = "nyssa-ai-configuration"
_KEY_BYTES: Final[int] = 32
_SANITIZE_RE: Final[re.Pattern[str]] = re.compile(r"[^A-Za-z0-9._-]")


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


def _linux_cred_exists(account_name: str, manifest_dir: Path | None) -> bool:
    if not sys.platform.startswith("linux") or not account_name.strip():
        return False
    sanitized_account = _SANITIZE_RE.sub("_", account_name.strip())
    cred_filename = f"{_SERVICE_NAME}.{sanitized_account}.cred"
    env_dir = os.environ.get("NYSSA_SYSTEMD_CREDS_DIR", "").strip()
    if env_dir:
        return (Path(env_dir) / cred_filename).is_file()
    default_manifest_dir = (Path.home() / ".nyssa-ai").resolve()
    if manifest_dir is not None and manifest_dir != default_manifest_dir:
        return False
    return (Path.home() / ".nyssa-ai" / "credentials" / cred_filename).is_file()


def _use_protected_file_fallback() -> bool:
    env_config_dir = os.environ.get("NYSSA_CONFIG_DIR")
    if env_config_dir and env_config_dir.strip():
        return True
    if Path("/.dockerenv").exists() or Path("/run/.containerenv").exists():
        return True
    return (
        sys.platform.startswith("linux")
        and not os.environ.get("DBUS_SESSION_BUS_ADDRESS")
        and not os.environ.get("DISPLAY")
        and not os.environ.get("WAYLAND_DISPLAY")
    )


def resolve_keyring(
    configuration_directory: str | Path,
    options: BootstrapOptions | None = None,
    keyring: Keyring | None = None,
    manifest_directory: str | Path | None = None,
) -> Keyring:
    """Resolve the active keyring strategy when no explicit keyring is provided."""
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
    resolved_manifest_dir: Path | None = None
    if manifest_directory is not None and str(manifest_directory).strip():
        resolved_manifest_dir = Path(str(manifest_directory)).resolve()
    elif options is not None and options.manifest_path and options.manifest_path.strip():
        resolved_manifest_dir = Path(options.manifest_path).resolve().parent
    elif options is not None and options.home_directory and options.home_directory.strip():
        resolved_manifest_dir = (Path(options.home_directory) / ".nyssa-ai").resolve()

    if (config_dir / _KEY_FILENAME).is_file():
        return KeyringProtectedFile(config_dir, manifest_directory=resolved_manifest_dir)

    account_candidate = (
        options.application_name.strip()
        if options is not None and options.application_name and options.application_name.strip()
        else (config_dir.name if os.environ.get("NYSSA_SYSTEMD_CREDS_DIR", "").strip() else "")
    )
    if account_candidate and _linux_cred_exists(account_candidate, resolved_manifest_dir):
        return KeyringDefault()

    if _use_protected_file_fallback():
        return KeyringProtectedFile(config_dir, manifest_directory=resolved_manifest_dir)

    return KeyringDefault()


def resolve_manifest_keyring(
    manifest_path: str | Path,
    keyring: Keyring | None = None,
) -> Keyring:
    """Resolve the keyring strategy for the shared '_manifest' encryption key."""
    if keyring is not None:
        return keyring

    from nyssa_ai_configuration.keyring.keyring_default import KeyringDefault
    from nyssa_ai_configuration.keyring.keyring_protected_file import (
        _MANIFEST_KEY_FILENAME,
        KeyringProtectedFile,
    )
    from nyssa_ai_configuration.keyring.keyring_systemd_creds import KeyringSystemdCreds

    if KeyringSystemdCreds.is_available():
        return KeyringSystemdCreds()

    manifest_dir = Path(str(manifest_path)).resolve().parent
    if (manifest_dir / _MANIFEST_KEY_FILENAME).is_file():
        return KeyringProtectedFile(manifest_dir, manifest_directory=manifest_dir)

    if _linux_cred_exists(MANIFEST_KEY_ACCOUNT, manifest_dir):
        return KeyringDefault()

    if _use_protected_file_fallback():
        return KeyringProtectedFile(manifest_dir, manifest_directory=manifest_dir)

    return KeyringDefault()


def get_existing_key(account_name: str, keyring: Keyring) -> Result[bytes | None]:
    """Read an existing 32-byte key from keyring without creating one when absent."""
    if not account_name or not account_name.strip():
        return fail("Account name cannot be blank.", "Validation")

    existing = keyring.get_key(_SERVICE_NAME, account_name)
    if not existing.ok:
        if existing.category == "NotFound":
            return ok(None)
        return existing
    if existing.value is None:
        return ok(None)
    if len(existing.value) != _KEY_BYTES:
        return fail(
            f"Stored key for '{account_name}' is {len(existing.value)} bytes; expected 32.",
            "Validation",
        )
    return ok(existing.value)


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
        return ok(existing.value)

    fresh = os.urandom(_KEY_BYTES)
    saved = keyring.set_key(_SERVICE_NAME, application_name, fresh)
    if not saved.ok:
        return saved
    return ok(fresh)
