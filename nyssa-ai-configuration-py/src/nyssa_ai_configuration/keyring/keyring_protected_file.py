# Responsibility: Store the 256-bit configuration master key in a restricted user-private file (.nyssa-key).
# Public interface:
# `-- KeyringProtectedFile
#     +-- delete_key(service, account)
#     +-- get_key(service, account)
#     +-- new(configuration_directory, manifest_directory)
#     `-- set_key(service, account, secret)
"""Protected local file Keyring implementation for headless and container environments."""

import base64
from pathlib import Path
from typing import Final

from nyssa_ai_configuration.file_security.directory_safety import DirectorySafety
from nyssa_ai_configuration.file_security.protected_file_system import ProtectedFileSystem
from nyssa_ai_result import Result, fail, ok

_KEY_FILENAME: Final[str] = ".nyssa-key"
_MANIFEST_KEY_FILENAME: Final[str] = ".nyssa-manifest-key"
_MANIFEST_ACCOUNT: Final[str] = "_manifest"


class KeyringProtectedFile:
    """Stores 32-byte master keys Base64-encoded inside private (0600 / DACL-restricted) files."""

    def __init__(
        self,
        configuration_directory: str | Path,
        manifest_directory: str | Path | None = None,
    ) -> None:
        """Initialize with the target configuration directory and optional manifest directory."""
        config_dir = Path(str(configuration_directory)).resolve()
        manifest_dir = (
            Path(str(manifest_directory)).resolve()
            if manifest_directory is not None and str(manifest_directory).strip()
            else config_dir
        )
        self.key_file_path = (config_dir / _KEY_FILENAME).resolve()
        self.manifest_key_file_path = (manifest_dir / _MANIFEST_KEY_FILENAME).resolve()

    def _path_for_account(self, account: str) -> Path:
        return self.manifest_key_file_path if account.strip() == _MANIFEST_ACCOUNT else self.key_file_path

    def get_key(self, service: str, account: str) -> Result[bytes | None]:
        """Read and validate the 32-byte key from the protected file, or return Ok(None) if absent."""
        del service
        target_path = self._path_for_account(account)
        if not target_path.exists():
            return ok(None)
        priv_res = DirectorySafety.verify_private_file(target_path)
        if not priv_res.ok:
            return priv_res
        try:
            b64 = target_path.read_text(encoding="utf-8").strip()
            key = base64.b64decode(b64, validate=True)
        except Exception as exc:
            return fail(f"Failed to read key file '{target_path}': {exc}", "Unavailable", exc)
        if len(key) != 32:
            return fail(
                f"Stored key in '{target_path}' is {len(key)} bytes; expected 32.",
                "Validation",
            )
        return ok(key)

    def set_key(self, service: str, account: str, secret: bytes) -> Result[None]:
        """Atomically write the 32-byte key to the protected file."""
        del service
        target_path = self._path_for_account(account)
        if len(secret) != 32:
            return fail(f"Key must be 32 bytes; {len(secret)} bytes supplied.", "Validation")
        b64 = base64.b64encode(secret).decode("ascii")
        write_res = ProtectedFileSystem.create_file(target_path, b64)
        if not write_res.ok:
            return write_res
        return ok(None)

    def delete_key(self, service: str, account: str) -> Result[None]:
        """Remove the protected key file if present."""
        del service
        target_path = self._path_for_account(account)
        try:
            if target_path.exists():
                target_path.unlink()
            return ok(None)
        except Exception as exc:
            return fail(f"Failed to delete key file '{target_path}': {exc}", "Unavailable", exc)
