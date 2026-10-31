# Responsibility: Persist 256-bit configuration master keys in the native OS credential store.
# Public interface:
# `-- KeyringDefault
#     +-- delete_key(service, account)
#     +-- get_key(service, account)
#     `-- set_key(service, account, secret)
"""OS credential store keyring (Windows Credential Manager, macOS Keychain, Linux Secret Service)."""

import base64
import ctypes
from ctypes import wintypes
import subprocess
import sys

from nyssa_ai_configuration.results.result import Result, fail, ok


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

    def get_key(self, service: str, account: str) -> Result[bytes | None]:
        """Read the 32-byte key from the OS credential store."""
        target = f"{service}/{account}"
        if sys.platform == "win32":
            return self._get_windows(target)
        if sys.platform == "darwin":
            return self._get_macos(service, account)
        if sys.platform.startswith("linux"):
            return self._get_linux(service, account)
        return fail(f"Unsupported OS platform '{sys.platform}'.", "Unavailable")

    def set_key(self, service: str, account: str, secret: bytes) -> Result[None]:
        """Store the 32-byte key in the OS credential store."""
        if len(secret) != 32:
            return fail(f"Key must be 32 bytes; {len(secret)} supplied.", "Validation")
        target = f"{service}/{account}"
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
        target = f"{service}/{account}"
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
                decoded = base64.b64decode(blob.decode("utf-16le"), validate=True)
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
        res = subprocess.run(
            ["/usr/bin/security", "find-generic-password", "-s", service, "-a", account, "-w"],
            capture_output=True,
            text=True,
            check=False,
        )
        if res.returncode == 44:
            return ok(None)
        if res.returncode != 0:
            return fail(f"SecItemCopyMatching failed: {res.stderr.strip()}", "Unavailable")
        try:
            return ok(base64.b64decode(res.stdout.strip(), validate=True))
        except Exception as exc:
            return fail(f"Corrupt macOS keychain entry: {exc}", "Validation", exc)

    @staticmethod
    def _set_macos(service: str, account: str, b64_secret: str) -> Result[None]:
        res = subprocess.run(
            ["/usr/bin/security", "add-generic-password", "-U", "-s", service, "-a", account, "-w", b64_secret],
            capture_output=True,
            text=True,
            check=False,
        )
        return ok(None) if res.returncode == 0 else fail(f"SecItemAdd failed: {res.stderr.strip()}", "Unavailable")

    @staticmethod
    def _delete_macos(service: str, account: str) -> Result[None]:
        subprocess.run(
            ["/usr/bin/security", "delete-generic-password", "-s", service, "-a", account],
            capture_output=True,
            check=False,
        )
        return ok(None)

    @staticmethod
    def _get_linux(service: str, account: str) -> Result[bytes | None]:
        res = subprocess.run(
            ["secret-tool", "lookup", "service", service, "username", account],
            capture_output=True,
            text=True,
            check=False,
        )
        if res.returncode != 0:
            return ok(None) if not res.stdout.strip() and not res.stderr.strip() else fail(f"Secret Service lookup failed: {res.stderr.strip()}", "Unavailable")
        out = res.stdout.strip()
        if not out:
            return ok(None)
        try:
            return ok(base64.b64decode(out, validate=True))
        except Exception as exc:
            return fail(f"Corrupt Secret Service entry: {exc}", "Validation", exc)

    @staticmethod
    def _set_linux(service: str, account: str, label: str, b64_secret: str) -> Result[None]:
        res = subprocess.run(
            ["secret-tool", "store", f"--label={label}", "service", service, "username", account],
            input=b64_secret,
            text=True,
            capture_output=True,
            check=False,
        )
        return ok(None) if res.returncode == 0 else fail(f"Secret Service write failed: {res.stderr.strip()}", "Unavailable")

    @staticmethod
    def _delete_linux(service: str, account: str) -> Result[None]:
        subprocess.run(
            ["secret-tool", "clear", "service", service, "username", account],
            capture_output=True,
            check=False,
        )
        return ok(None)
