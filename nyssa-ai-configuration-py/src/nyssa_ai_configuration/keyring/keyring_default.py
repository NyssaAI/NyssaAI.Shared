# Responsibility: Persist 256-bit configuration master keys in the native OS credential store.
# Public interface:
# `-- KeyringDefault
#     +-- delete_key(service, account)
#     +-- get_key(service, account)
#     +-- set_key(service, account, secret)
#     `-- validate_address(service, account)
"""OS credential store keyring (Windows Credential Manager, macOS Keychain, Linux systemd-creds)."""

import base64
import ctypes
import os
import re
import subprocess
import sys
from ctypes import wintypes
from pathlib import Path
from typing import Final

from nyssa_ai_configuration.keyring.keyring_systemd_creds import KeyringSystemdCreds
from nyssa_ai_configuration.results.result import Result, fail, ok

_MAX_ADDRESS_LENGTH: Final[int] = 1024
_CONTROL_CHAR_RE: Final[re.Pattern[str]] = re.compile(r"[\x00-\x1f\x7f]")
_SANITIZE_RE: Final[re.Pattern[str]] = re.compile(r"[^A-Za-z0-9._-]")


def _sanitize(value: str) -> str:
    return _SANITIZE_RE.sub("_", value)


def _win_credential_type() -> type[ctypes.Structure]:
    class _Cred(ctypes.Structure):
        _fields_ = [
            ("Flags", wintypes.DWORD),
            ("Type", wintypes.DWORD),
            ("TargetName", wintypes.LPWSTR),
            ("Comment", wintypes.LPWSTR),
            ("LastWritten", wintypes.FILETIME),
            ("CredentialBlobSize", wintypes.DWORD),
            ("CredentialBlob", ctypes.c_void_p),
            ("Persist", wintypes.DWORD),
            ("AttributeCount", wintypes.DWORD),
            ("Attributes", ctypes.c_void_p),
            ("TargetAlias", wintypes.LPWSTR),
            ("UserName", wintypes.LPWSTR),
        ]

    return _Cred


class KeyringDefault:
    """Platform OS credential store adapter."""

    @staticmethod
    def validate_address(service: str, account: str) -> Result[str]:
        """Validate service and account and compose the service/account credential address."""
        if not service or not service.strip():
            return fail("keyring: service name must not be blank.", "Validation")
        if not account or not account.strip():
            return fail("keyring: key name must not be blank.", "Validation")
        if _CONTROL_CHAR_RE.search(service) or _CONTROL_CHAR_RE.search(account):
            return fail("keyring: service and key must not contain control characters.", "Validation")
        target = f"{service}/{account}"
        if len(target) > _MAX_ADDRESS_LENGTH:
            return fail(f"keyring: the address is longer than {_MAX_ADDRESS_LENGTH} characters.", "Validation")
        return ok(target)

    def get_key(self, service: str, account: str) -> Result[bytes | None]:
        """Read the 32-byte key from the OS credential store."""
        addr_res = self.validate_address(service, account)
        if not addr_res.ok:
            return addr_res
        target = addr_res.value
        if sys.platform == "win32":
            return self._get_windows(target)
        if sys.platform == "darwin":
            return self._get_macos(service, account)
        if sys.platform.startswith("linux"):
            return self._get_linux(service, account)
        return fail(f"Unsupported OS platform '{sys.platform}'.", "Unavailable")

    def set_key(self, service: str, account: str, secret: bytes) -> Result[None]:
        """Store the 32-byte key in the OS credential store."""
        addr_res = self.validate_address(service, account)
        if not addr_res.ok:
            return addr_res
        if len(secret) != 32:
            return fail(f"Key must be 32 bytes; {len(secret)} supplied.", "Validation")
        target = addr_res.value
        b64 = base64.b64encode(secret).decode("ascii")
        if sys.platform == "win32":
            return self._set_windows(target, b64)
        if sys.platform == "darwin":
            return self._set_macos(service, account, b64)
        if sys.platform.startswith("linux"):
            return self._set_linux(service, account, target, b64)
        return fail(f"Unsupported OS platform '{sys.platform}'.", "Unavailable")

    def delete_key(self, service: str, account: str) -> Result[None]:
        """Delete the key from the OS credential store."""
        addr_res = self.validate_address(service, account)
        if not addr_res.ok:
            return addr_res
        target = addr_res.value
        if sys.platform == "win32":
            return self._delete_windows(target)
        if sys.platform == "darwin":
            return self._delete_macos(service, account)
        if sys.platform.startswith("linux"):
            return self._delete_linux(service, account)
        return fail(f"Unsupported OS platform '{sys.platform}'.", "Unavailable")

    @staticmethod
    def _get_windows(target: str) -> Result[bytes | None]:
        try:
            advapi32 = ctypes.windll.advapi32
            cred_cls = _win_credential_type()
            cred_ptr = ctypes.POINTER(cred_cls)()
            res = advapi32.CredReadW(target, 1, 0, ctypes.byref(cred_ptr))
            if not res:
                err = ctypes.GetLastError()
                return ok(None) if err == 1168 else fail(f"CredReadW failed with error {err}.", "Unavailable")
            try:
                cred = cred_ptr.contents
                blob = ctypes.string_at(cred.CredentialBlob, cred.CredentialBlobSize)
                try:
                    decoded = base64.b64decode(blob.decode("utf-16le"), validate=True)
                except Exception as exc:
                    return fail(f"Corrupt Windows credential entry: {exc}", "Validation", exc)
                if len(decoded) != 32:
                    return fail(f"Stored Windows credential is {len(decoded)} bytes; expected 32.", "Validation")
                return ok(decoded)
            finally:
                advapi32.CredFree(cred_ptr)
        except Exception as exc:
            return fail(f"Windows keyring read error: {exc}", "Unavailable", exc)

    @staticmethod
    def _set_windows(target: str, b64_secret: str) -> Result[None]:
        try:
            advapi32 = ctypes.windll.advapi32
            cred_cls = _win_credential_type()
            blob = b64_secret.encode("utf-16le")
            cred = cred_cls()
            cred.Flags = 0
            cred.Type = 1
            cred.TargetName = target
            cred.CredentialBlobSize = len(blob)
            cred.CredentialBlob = ctypes.cast(ctypes.c_char_p(blob), ctypes.c_void_p)
            cred.Persist = 2
            res = advapi32.CredWriteW(ctypes.byref(cred), 0)
            if not res:
                return fail(f"CredWriteW failed with error {ctypes.GetLastError()}.", "Unavailable")
            return ok(None)
        except Exception as exc:
            return fail(f"Windows keyring write error: {exc}", "Unavailable", exc)

    @staticmethod
    def _delete_windows(target: str) -> Result[None]:
        try:
            advapi32 = ctypes.windll.advapi32
            res = advapi32.CredDeleteW(target, 1, 0)
            if not res:
                err = ctypes.GetLastError()
                if err != 1168:
                    return fail(f"CredDeleteW failed with error {err}.", "Unavailable")
            return ok(None)
        except Exception as exc:
            return fail(f"Windows keyring delete error: {exc}", "Unavailable", exc)

    @staticmethod
    def _get_macos(service: str, account: str) -> Result[bytes | None]:
        try:
            res = subprocess.run(
                ["/usr/bin/security", "find-generic-password", "-s", service, "-a", account, "-w"],
                capture_output=True,
                text=True,
                check=False,
            )
        except Exception as exc:
            return fail(f"SecItemCopyMatching failed: {exc}", "Unavailable", exc)
        if res.returncode == 44:
            return ok(None)
        if res.returncode != 0:
            return fail(f"SecItemCopyMatching failed: {res.stderr.strip()}", "Unavailable")
        try:
            decoded = base64.b64decode(res.stdout.strip(), validate=True)
        except Exception as exc:
            return fail(f"Corrupt macOS keychain entry: {exc}", "Validation", exc)
        if len(decoded) != 32:
            return fail(f"Stored macOS keychain entry is {len(decoded)} bytes; expected 32.", "Validation")
        return ok(decoded)

    @staticmethod
    def _set_macos(service: str, account: str, b64_secret: str) -> Result[None]:
        try:
            res = subprocess.run(
                ["/usr/bin/security", "add-generic-password", "-U", "-s", service, "-a", account, "-w", b64_secret],
                capture_output=True,
                text=True,
                check=False,
            )
        except Exception as exc:
            return fail(f"SecItemAdd failed: {exc}", "Unavailable", exc)
        return ok(None) if res.returncode == 0 else fail(f"SecItemAdd failed: {res.stderr.strip()}", "Unavailable")

    @staticmethod
    def _delete_macos(service: str, account: str) -> Result[None]:
        try:
            res = subprocess.run(
                ["/usr/bin/security", "delete-generic-password", "-s", service, "-a", account],
                capture_output=True,
                text=True,
                check=False,
            )
        except Exception as exc:
            return fail(f"SecItemDelete failed: {exc}", "Unavailable", exc)
        if res.returncode not in (0, 44):
            return fail(f"SecItemDelete failed: {res.stderr.strip()}", "Unavailable")
        return ok(None)

    @staticmethod
    def _linux_cred_paths(service: str, account: str) -> tuple[str, Path, Path]:
        cred_name = f"{_sanitize(service)}.{_sanitize(account)}"
        env_dir = os.environ.get("NYSSA_SYSTEMD_CREDS_DIR", "").strip()
        creds_store_dir = Path(env_dir) if env_dir else Path.home() / ".nyssa-ai" / "credentials"
        cred_file = creds_store_dir / f"{cred_name}.cred"
        return cred_name, creds_store_dir, cred_file

    @staticmethod
    def _systemd_user_args() -> list[str]:
        geteuid = getattr(os, "geteuid", None)
        return [] if callable(geteuid) and geteuid() == 0 else ["--user"]

    @staticmethod
    def _get_linux(service: str, account: str) -> Result[bytes | None]:
        if KeyringSystemdCreds.is_available():
            delegated = KeyringSystemdCreds().get_key(service, account)
            if delegated.ok or delegated.category != "NotFound":
                return delegated
        cred_name, _, cred_file = KeyringDefault._linux_cred_paths(service, account)
        if not cred_file.is_file():
            return ok(None)
        try:
            res = subprocess.run(
                [
                    "systemd-creds",
                    *KeyringDefault._systemd_user_args(),
                    "decrypt",
                    f"--name={cred_name}",
                    str(cred_file),
                    "-",
                ],
                capture_output=True,
                text=True,
                check=False,
            )
        except Exception as exc:
            return fail(f"systemd-creds decrypt failed: {exc}", "Unavailable", exc)
        if res.returncode != 0:
            return fail(f"systemd-creds decrypt failed: {res.stderr.strip()}", "Unavailable")
        out = res.stdout.strip()
        if not out:
            return fail(f"Corrupt systemd-creds entry for '{cred_name}': empty output.", "Validation")
        if len(res.stdout) == 32 and len(out) == 32:
            return ok(res.stdout.encode("latin-1"))
        try:
            decoded = base64.b64decode(out, validate=True)
        except Exception as exc:
            return fail(f"Corrupt systemd-creds entry: {exc}", "Validation", exc)
        if len(decoded) != 32:
            return fail(f"Stored systemd-creds entry is {len(decoded)} bytes; expected 32.", "Validation")
        return ok(decoded)

    @staticmethod
    def _set_linux(service: str, account: str, label: str, b64_secret: str) -> Result[None]:
        del label
        cred_name, creds_store_dir, cred_file = KeyringDefault._linux_cred_paths(service, account)
        temp_file = creds_store_dir / f".{cred_name}.{os.getpid()}.tmp"
        try:
            creds_store_dir.mkdir(parents=True, exist_ok=True, mode=0o700)
            res = subprocess.run(
                [
                    "systemd-creds",
                    *KeyringDefault._systemd_user_args(),
                    "encrypt",
                    f"--name={cred_name}",
                    "-",
                    str(temp_file),
                ],
                input=b64_secret,
                text=True,
                capture_output=True,
                check=False,
            )
            if res.returncode != 0:
                if temp_file.exists():
                    temp_file.unlink()
                return fail(f"systemd-creds encrypt failed: {res.stderr.strip()}", "Unavailable")
            if temp_file.exists():
                os.chmod(temp_file, 0o600)
                temp_file.replace(cred_file)
            return ok(None)
        except Exception as exc:
            if temp_file.exists():
                try:
                    temp_file.unlink()
                except OSError:
                    pass
            return fail(f"systemd-creds encrypt failed: {exc}", "Unavailable", exc)

    @staticmethod
    def _delete_linux(service: str, account: str) -> Result[None]:
        _, _, cred_file = KeyringDefault._linux_cred_paths(service, account)
        try:
            if cred_file.exists():
                cred_file.unlink()
            return ok(None)
        except Exception as exc:
            return fail(f"systemd-creds delete failed: {exc}", "Unavailable", exc)
