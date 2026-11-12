# Responsibility: Read the 32-byte master encryption key from systemd encrypted credentials (CREDENTIALS_DIRECTORY).
# Public interface:
# `-- KeyringSystemdCreds
#     +-- IMMUTABLE_ERROR_MESSAGE
#     +-- delete_key(service, account)
#     +-- get_key(service, account)
#     +-- is_available()
#     +-- new(credential_name)
#     `-- set_key(service, account, secret)
"""Keyring adapter for systemd encrypted credentials (CREDENTIALS_DIRECTORY)."""

import base64
import os
import re
from pathlib import Path
from typing import Final

from nyssa_ai_result import Result, fail, ok

_BASE64_RE: Final[re.Pattern[str]] = re.compile(r"^[A-Za-z0-9+/]+={0,2}$")
_CONTROL_CHAR_RE: Final[re.Pattern[str]] = re.compile(r"[\x00-\x1f\x7f]")
_SANITIZE_RE: Final[re.Pattern[str]] = re.compile(r"[^A-Za-z0-9._-]")
_MANIFEST_ACCOUNT: Final[str] = "_manifest"
_MANIFEST_CREDENTIAL_NAMES: Final[tuple[str, ...]] = (
    "nyssa-ai-configuration._manifest",
    "_manifest.key",
    "_manifest",
    "nyssa-manifest-key",
    "nyssa-manifest-key.key",
)


class KeyringSystemdCreds:
    """Reads the 256-bit (32-byte) master key from the RAMFS directory provisioned by systemd (CREDENTIALS_DIRECTORY)."""

    IMMUTABLE_ERROR_MESSAGE: str = (
        "systemd-creds is immutable at runtime; credentials must be provisioned via systemd service configuration."
    )

    def __init__(self, credential_name: str | None = None) -> None:
        """Initialize with an optional explicit systemd credential filename."""
        trimmed = credential_name.strip() if credential_name is not None else ""
        self._credential_name: str | None = trimmed if trimmed else None

    @staticmethod
    def is_available() -> bool:
        """Return True if CREDENTIALS_DIRECTORY is set and points to an existing directory."""
        raw_dir = os.environ.get("CREDENTIALS_DIRECTORY")
        if not raw_dir or not raw_dir.strip():
            return False
        try:
            return Path(raw_dir.strip()).is_dir()
        except OSError:
            return False

    def get_key(self, service: str, account: str) -> Result[bytes | None]:
        """Read and validate the 32-byte master key from CREDENTIALS_DIRECTORY."""
        if not service or not service.strip() or not account or not account.strip():
            return fail("keyring: service and account must be non-empty.", "Validation")
        if _CONTROL_CHAR_RE.search(service) or _CONTROL_CHAR_RE.search(account):
            return fail("keyring: service and account must not contain control characters.", "Validation")

        raw_dir = os.environ.get("CREDENTIALS_DIRECTORY")
        if not raw_dir or not raw_dir.strip():
            return fail("systemd-creds: CREDENTIALS_DIRECTORY is not set.", "NotFound")

        credentials_dir = Path(raw_dir.strip()).resolve()
        if not KeyringSystemdCreds.is_available():
            return fail(
                f"systemd-creds: CREDENTIALS_DIRECTORY '{credentials_dir}' does not exist.",
                "NotFound",
            )

        file_res = self._resolve_credential_file(credentials_dir, service.strip(), account.strip())
        if not file_res.ok:
            return file_res

        try:
            raw_bytes = file_res.value.read_bytes()
            return self._parse_key_material(raw_bytes, file_res.value)
        except Exception as exc:
            return fail(
                f"systemd-creds: failed to read '{file_res.value}': {exc}",
                "Unavailable",
                exc,
            )

    def set_key(self, service: str, account: str, secret: bytes) -> Result[None]:
        """Reject runtime writes because systemd credentials are immutable at runtime."""
        del service, account, secret
        return fail(self.IMMUTABLE_ERROR_MESSAGE, "Forbidden")

    def delete_key(self, service: str, account: str) -> Result[None]:
        """Reject runtime deletions because systemd credentials are immutable at runtime."""
        del service, account
        return fail(self.IMMUTABLE_ERROR_MESSAGE, "Forbidden")

    def _resolve_credential_file(self, credentials_dir: Path, service: str, account: str) -> Result[Path]:
        is_manifest = account == _MANIFEST_ACCOUNT
        sanitized_service = _SANITIZE_RE.sub("_", service)
        sanitized_account = _SANITIZE_RE.sub("_", account)
        candidates: tuple[str, ...]
        if is_manifest:
            candidates = (f"{sanitized_service}._manifest", *_MANIFEST_CREDENTIAL_NAMES)
        elif self._credential_name is not None:
            candidates = (self._credential_name, f"{self._credential_name}.key")
        else:
            sanitized_pair = f"{sanitized_service}.{sanitized_account}"
            candidates = (
                sanitized_pair,
                f"{sanitized_pair}.key",
                f"{service}.key",
                f"{account}.key",
                service,
                account,
                "nyssa-key",
                "nyssa-key.key",
                "master-key",
                "master-key.key",
            )

        for name in candidates:
            if "/" in name or "\\" in name or name in (".", ".."):
                continue
            candidate_path = credentials_dir / name
            try:
                if candidate_path.parent.resolve() == credentials_dir and candidate_path.is_file():
                    return ok(candidate_path)
            except OSError:
                continue

        if self._credential_name is None and not is_manifest:
            excluded = set(_MANIFEST_CREDENTIAL_NAMES) | {f"{sanitized_service}._manifest"}
            try:
                files = [
                    entry
                    for entry in credentials_dir.iterdir()
                    if entry.is_file()
                    and entry.name not in excluded
                    and not entry.name.endswith("._manifest")
                ]
                if len(files) == 1:
                    return ok(files[0])
            except Exception as exc:
                return fail(
                    f"systemd-creds: failed to list '{credentials_dir}': {exc}",
                    "Unavailable",
                    exc,
                )

        return fail(
            f"systemd-creds: no credential file found for '{service}/{account}' in '{credentials_dir}'.",
            "NotFound",
        )

    @staticmethod
    def _parse_key_material(raw_bytes: bytes, file_path: Path) -> Result[bytes | None]:
        if len(raw_bytes) == 0:
            return fail(f"systemd-creds: credential file '{file_path}' is empty.", "Validation")

        if len(raw_bytes) == 32:
            return ok(bytes(raw_bytes))

        try:
            trimmed = raw_bytes.decode("utf-8").strip()
        except UnicodeDecodeError as exc:
            return fail(
                f"systemd-creds: credential file '{file_path}' contains malformed base64 or invalid key length.",
                "Validation",
                exc,
            )

        if not trimmed:
            return fail(f"systemd-creds: credential file '{file_path}' is empty.", "Validation")

        if len(trimmed) % 4 != 0 or not _BASE64_RE.match(trimmed):
            return fail(
                f"systemd-creds: credential file '{file_path}' contains malformed base64 or invalid key length.",
                "Validation",
            )

        try:
            decoded = base64.b64decode(trimmed, validate=True)
        except Exception as exc:
            return fail(
                f"systemd-creds: credential file '{file_path}' contains malformed base64 or invalid key length.",
                "Validation",
                exc,
            )

        if len(decoded) != 32:
            return fail(
                f"systemd-creds: credential file '{file_path}' decoded to {len(decoded)} bytes; expected 32 bytes.",
                "Validation",
            )

        return ok(decoded)
