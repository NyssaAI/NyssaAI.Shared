# Responsibility: Verify KeyringDefault OS credential backends (Windows CredMan, macOS Keychain, Linux systemd-creds) and resolve_keyring.
# Public interface:
# +-- test_keyring_default_linux_systemd_creds_encrypt_decrypt_delete_and_error_classification(tmp_path, monkeypatch)
# +-- test_keyring_default_macos_keychain_find_add_delete_and_error_classification(monkeypatch)
# +-- test_keyring_default_unsupported_platform_returns_unavailable(monkeypatch)
# +-- test_keyring_default_validate_address_rejects_blank_control_and_overlong_addresses(service, account)
# +-- test_keyring_default_windows_credman_read_write_delete_and_error_classification(monkeypatch)
# `-- test_resolve_keyring_selects_expected_backend_across_environments(tmp_path, monkeypatch)
"""Unit tests for KeyringDefault OS credential store adapters and resolve_keyring."""

import base64
import ctypes
import os
import subprocess
from pathlib import Path

import pytest

from nyssa_ai_configuration.keyring.keyring import resolve_keyring
from nyssa_ai_configuration.keyring.keyring_default import KeyringDefault, _win_credential_type
from nyssa_ai_configuration.keyring.keyring_in_memory import KeyringInMemory
from nyssa_ai_configuration.keyring.keyring_protected_file import KeyringProtectedFile


@pytest.mark.parametrize(
    ("service", "account"),
    [("", "account"), ("   ", "account"), ("service", ""), ("service", "   "), ("serv\x00ice", "account"), ("service", "acc\nount"), ("service", "acc\x7fount"), ("s" * 600, "a" * 600)],
)
def test_keyring_default_validate_address_rejects_blank_control_and_overlong_addresses(service: str, account: str) -> None:
    """Verify KeyringDefault rejects blank, control-character, and >1024-char addresses across get/set/delete."""
    keyring = KeyringDefault()
    secret = os.urandom(32)
    for res in (
        KeyringDefault.validate_address(service, account),
        keyring.get_key(service, account),
        keyring.set_key(service, account, secret),
        keyring.delete_key(service, account),
    ):
        assert not res.ok and res.category == "Validation"


def test_keyring_default_unsupported_platform_returns_unavailable(monkeypatch: pytest.MonkeyPatch) -> None:
    """Verify KeyringDefault returns Unavailable on unsupported OS platforms."""
    monkeypatch.setattr("nyssa_ai_configuration.keyring.keyring_default.sys.platform", "freebsd14")
    keyring = KeyringDefault()
    secret = os.urandom(32)
    for res in (
        keyring.get_key("nyssa-ai-configuration", "OrdersService"),
        keyring.set_key("nyssa-ai-configuration", "OrdersService", secret),
        keyring.delete_key("nyssa-ai-configuration", "OrdersService"),
    ):
        assert not res.ok and res.category == "Unavailable"


def test_keyring_default_macos_keychain_find_add_delete_and_error_classification(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Verify macOS Keychain subprocess invocations, exit code 44 absence, base64 validation, and error handling."""
    monkeypatch.setattr("nyssa_ai_configuration.keyring.keyring_default.sys.platform", "darwin")
    keyring = KeyringDefault()
    secret = os.urandom(32)
    b64_secret = base64.b64encode(secret).decode("ascii")

    store: dict[tuple[str, str], str] = {}
    simulated_Exit: int | None = None

    def fake_run(
        args: list[str], capture_output: bool = False, text: bool = False, check: bool = False, input: str | None = None
    ) -> subprocess.CompletedProcess[str]:
        del capture_output, text, check, input
        assert args[0] == "/usr/bin/security"
        cmd = args[1]
        if simulated_Exit is not None:
            return subprocess.CompletedProcess(args, simulated_Exit, stdout="", stderr="errSecInteractionNotAllowed")
        svc, acct = args[args.index("-s") + 1], args[args.index("-a") + 1]
        if cmd == "find-generic-password":
            if (svc, acct) not in store:
                return subprocess.CompletedProcess(args, 44, stdout="", stderr="SecKeychainSearchCopyNext: item not found")
            return subprocess.CompletedProcess(args, 0, stdout=store[(svc, acct)] + "\n", stderr="")
        if cmd == "add-generic-password":
            assert "-U" in args
            store[(svc, acct)] = args[args.index("-w") + 1]
            return subprocess.CompletedProcess(args, 0, stdout="", stderr="")
        if cmd == "delete-generic-password":
            if (svc, acct) not in store:
                return subprocess.CompletedProcess(args, 44, stdout="", stderr="")
            del store[(svc, acct)]
            return subprocess.CompletedProcess(args, 0, stdout="", stderr="")
        raise AssertionError(f"Unexpected security subcommand: {cmd}")

    monkeypatch.setattr("nyssa_ai_configuration.keyring.keyring_default.subprocess.run", fake_run)

    # 1. Absent key (exit code 44) -> Ok(None)
    absent = keyring.get_key("nyssa-ai-configuration", "OrdersService")
    assert absent.ok and absent.value is None

    # 2. Set and get round-trip
    assert keyring.set_key("nyssa-ai-configuration", "OrdersService", secret).ok
    assert store[("nyssa-ai-configuration", "OrdersService")] == b64_secret
    found = keyring.get_key("nyssa-ai-configuration", "OrdersService")
    assert found.ok and found.value == secret

    # 3. Corrupt base64 or wrong decoded length -> Validation
    for bad_val in ("!!!not-base64!!!", base64.b64encode(b"short-16-bytes!!").decode("ascii")):
        store[("nyssa-ai-configuration", "OrdersService")] = bad_val
        bad_res = keyring.get_key("nyssa-ai-configuration", "OrdersService")
        assert not bad_res.ok and bad_res.category == "Validation"

    # 4. Delete existing and absent (exit code 44 is idempotent)
    assert keyring.delete_key("nyssa-ai-configuration", "OrdersService").ok
    assert keyring.delete_key("nyssa-ai-configuration", "OrdersService").ok

    # 5. Non-44 non-zero exit code -> Unavailable (fail-closed custody)
    simulated_Exit = 36
    for res in (
        keyring.get_key("nyssa-ai-configuration", "OrdersService"),
        keyring.set_key("nyssa-ai-configuration", "OrdersService", secret),
        keyring.delete_key("nyssa-ai-configuration", "OrdersService"),
    ):
        assert not res.ok and res.category == "Unavailable"


def test_keyring_default_linux_systemd_creds_encrypt_decrypt_delete_and_error_classification(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    """Verify Linux systemd-creds encrypt/decrypt, absent .cred file, CREDENTIALS_DIRECTORY delegation, and error classification."""
    monkeypatch.setattr("nyssa_ai_configuration.keyring.keyring_default.sys.platform", "linux")
    monkeypatch.delenv("CREDENTIALS_DIRECTORY", raising=False)
    creds_dir = tmp_path / "systemd-creds-store"
    monkeypatch.setenv("NYSSA_SYSTEMD_CREDS_DIR", str(creds_dir))
    keyring = KeyringDefault()
    secret = os.urandom(32)
    cred_file = creds_dir / "nyssa-ai-configuration.OrdersService.cred"
    simulated_error: tuple[int, str] | None = None

    def fake_run(
        args: list[str], capture_output: bool = False, text: bool = False, check: bool = False, input: str | None = None
    ) -> subprocess.CompletedProcess[str]:
        del capture_output, text, check
        assert args[0] == "systemd-creds"
        if simulated_error is not None:
            return subprocess.CompletedProcess(args, simulated_error[0], stdout="", stderr=simulated_error[1])
        if "encrypt" in args:
            assert "--name=nyssa-ai-configuration.OrdersService" in args
            assert input is not None
            Path(args[-1]).write_text(f"ENC:{input}", encoding="utf-8")
            return subprocess.CompletedProcess(args, 0, stdout="", stderr="")
        if "decrypt" in args:
            assert "--name=nyssa-ai-configuration.OrdersService" in args
            raw = Path(args[-2]).read_text(encoding="utf-8")
            return subprocess.CompletedProcess(args, 0, stdout=raw.removeprefix("ENC:") + "\n", stderr="")
        raise AssertionError(f"Unexpected systemd-creds command: {args}")

    monkeypatch.setattr("nyssa_ai_configuration.keyring.keyring_default.subprocess.run", fake_run)

    # 1. Absent .cred file -> Ok(None) without invoking systemd-creds
    absent = keyring.get_key("nyssa-ai-configuration", "OrdersService")
    assert absent.ok and absent.value is None

    # 2. Set (systemd-creds encrypt) creates .cred file and get (systemd-creds decrypt) round-trips
    assert keyring.set_key("nyssa-ai-configuration", "OrdersService", secret).ok
    assert cred_file.is_file()
    found = keyring.get_key("nyssa-ai-configuration", "OrdersService")
    assert found.ok and found.value == secret

    # 3. CREDENTIALS_DIRECTORY delegation takes priority when present
    runtime_creds = tmp_path / "run-credentials"
    runtime_creds.mkdir()
    delegated_secret = os.urandom(32)
    (runtime_creds / "OrdersService.key").write_bytes(delegated_secret)
    monkeypatch.setenv("CREDENTIALS_DIRECTORY", str(runtime_creds))
    delegated = keyring.get_key("nyssa-ai-configuration", "OrdersService")
    assert delegated.ok and delegated.value == delegated_secret
    monkeypatch.delenv("CREDENTIALS_DIRECTORY", raising=False)

    # 4. Corrupt decrypted payload or wrong decoded length -> Validation
    for bad_val in ("ENC:***corrupt***", "ENC:" + base64.b64encode(b"1234567890123456").decode("ascii")):
        cred_file.write_text(bad_val, encoding="utf-8")
        bad_res = keyring.get_key("nyssa-ai-configuration", "OrdersService")
        assert not bad_res.ok and bad_res.category == "Validation"

    # 5. Non-zero systemd-creds exit -> Unavailable (fail-closed custody)
    cred_file.write_text("ENC:" + base64.b64encode(secret).decode("ascii"), encoding="utf-8")
    simulated_error = (1, "Failed to acquire TPM2/host key")
    for res in (
        keyring.get_key("nyssa-ai-configuration", "OrdersService"),
        keyring.set_key("nyssa-ai-configuration", "OrdersService", secret),
    ):
        assert not res.ok and res.category == "Unavailable"

    # 6. Delete removes <cred_name>.cred
    simulated_error = None
    assert keyring.delete_key("nyssa-ai-configuration", "OrdersService").ok
    assert not cred_file.exists()
    assert keyring.get_key("nyssa-ai-configuration", "OrdersService").value is None


def test_keyring_default_windows_credman_read_write_delete_and_error_classification(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Verify Windows CredMan CredReadW/CredWriteW/CredDeleteW UTF-16LE blob marshaling and Win32 error codes."""
    monkeypatch.setattr("nyssa_ai_configuration.keyring.keyring_default.sys.platform", "win32")
    keyring = KeyringDefault()
    secret = os.urandom(32)

    cred_cls = _win_credential_type()
    stored_blobs: dict[str, bytes] = {}
    last_error: int = 0
    simulated_win32_error: int | None = None
    freed_pointers: list[int] = []

    class FakeAdvapi32:
        def CredReadW(self, target: str, cred_type: int, flags: int, ptr_ref: object) -> int:
            nonlocal last_error
            assert cred_type == 1 and flags == 0
            if simulated_win32_error is not None:
                last_error = simulated_win32_error
                return 0
            if target not in stored_blobs:
                last_error = 1168  # ERROR_NOT_FOUND
                return 0
            raw_blob = stored_blobs[target]
            buf = ctypes.create_string_buffer(raw_blob, len(raw_blob))
            self._pinned_buf = buf
            cred_inst = cred_cls()
            cred_inst.CredentialBlobSize = len(raw_blob)
            cred_inst.CredentialBlob = ctypes.cast(buf, ctypes.c_void_p)
            self._pinned_cred = cred_inst
            target_ptr = ctypes.cast(ptr_ref, ctypes.POINTER(ctypes.POINTER(cred_cls)))  # type: ignore[arg-type]
            target_ptr.contents.contents = cred_inst
            last_error = 0
            return 1

        def CredWriteW(self, cred_ref: object, flags: int) -> int:
            nonlocal last_error
            assert flags == 0
            if simulated_win32_error is not None:
                last_error = simulated_win32_error
                return 0
            cred_ptr = ctypes.cast(cred_ref, ctypes.POINTER(cred_cls))  # type: ignore[arg-type]
            cred = cred_ptr.contents
            assert cred.Type == 1
            assert cred.Persist == 2
            blob = ctypes.string_at(cred.CredentialBlob, cred.CredentialBlobSize)
            stored_blobs[str(cred.TargetName)] = blob
            last_error = 0
            return 1

        def CredDeleteW(self, target: str, cred_type: int, flags: int) -> int:
            nonlocal last_error
            assert cred_type == 1 and flags == 0
            if simulated_win32_error is not None:
                last_error = simulated_win32_error
                return 0
            if target not in stored_blobs:
                last_error = 1168
                return 0
            del stored_blobs[target]
            last_error = 0
            return 1

        def CredFree(self, ptr: object) -> None:
            del ptr
            freed_pointers.append(1)

    class FakeWinDll:
        def __init__(self) -> None:
            self.advapi32 = FakeAdvapi32()

    monkeypatch.setattr("nyssa_ai_configuration.keyring.keyring_default.ctypes.windll", FakeWinDll(), raising=False)
    monkeypatch.setattr("nyssa_ai_configuration.keyring.keyring_default.ctypes.GetLastError", lambda: last_error, raising=False)

    # 1. Absent credential (ERROR_NOT_FOUND = 1168) -> Ok(None)
    absent = keyring.get_key("nyssa-ai-configuration", "OrdersService")
    assert absent.ok and absent.value is None

    # 2. Write and read back UTF-16LE Base64 credential
    assert keyring.set_key("nyssa-ai-configuration", "OrdersService", secret).ok
    raw_stored = stored_blobs["nyssa-ai-configuration/OrdersService"]
    assert raw_stored == base64.b64encode(secret).decode("ascii").encode("utf-16le")

    found = keyring.get_key("nyssa-ai-configuration", "OrdersService")
    assert found.ok and found.value == secret
    assert len(freed_pointers) == 1

    # 3. Corrupt UTF-16LE Base64 or wrong key length -> Validation
    for bad_str in ("not-valid-base64!!!", base64.b64encode(b"only-16-bytes!!!").decode("ascii")):
        stored_blobs["nyssa-ai-configuration/OrdersService"] = bad_str.encode("utf-16le")
        bad_res = keyring.get_key("nyssa-ai-configuration", "OrdersService")
        assert not bad_res.ok and bad_res.category == "Validation"

    # 4. Delete existing and absent (ERROR_NOT_FOUND = 1168 is idempotent)
    assert keyring.delete_key("nyssa-ai-configuration", "OrdersService").ok
    assert keyring.delete_key("nyssa-ai-configuration", "OrdersService").ok

    # 5. Non-1168 Win32 error (e.g., ERROR_ACCESS_DENIED = 5) -> Unavailable (fail-closed)
    simulated_win32_error = 5
    for res in (
        keyring.get_key("nyssa-ai-configuration", "OrdersService"),
        keyring.set_key("nyssa-ai-configuration", "OrdersService", secret),
        keyring.delete_key("nyssa-ai-configuration", "OrdersService"),
    ):
        assert not res.ok and res.category == "Unavailable"


def test_resolve_keyring_selects_expected_backend_across_environments(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    """Verify resolve_keyring priority across explicit keyring, NYSSA_CONFIG_DIR, headless Linux, and desktop."""
    monkeypatch.delenv("CREDENTIALS_DIRECTORY", raising=False)
    monkeypatch.delenv("NYSSA_CONFIG_DIR", raising=False)

    config_dir = tmp_path / "app-config"
    config_dir.mkdir(parents=True, exist_ok=True)

    # 1. Explicit keyring takes precedence
    explicit = KeyringInMemory()
    assert resolve_keyring(config_dir, keyring=explicit) is explicit

    # 2. NYSSA_CONFIG_DIR selects KeyringProtectedFile
    monkeypatch.setenv("NYSSA_CONFIG_DIR", str(config_dir))
    assert isinstance(resolve_keyring(config_dir), KeyringProtectedFile)
    monkeypatch.delenv("NYSSA_CONFIG_DIR", raising=False)

    # 3. Headless Linux without D-Bus/DISPLAY/WAYLAND_DISPLAY selects KeyringProtectedFile
    monkeypatch.setattr("nyssa_ai_configuration.keyring.keyring.sys.platform", "linux")
    monkeypatch.delenv("DBUS_SESSION_BUS_ADDRESS", raising=False)
    monkeypatch.delenv("DISPLAY", raising=False)
    monkeypatch.delenv("WAYLAND_DISPLAY", raising=False)
    assert isinstance(resolve_keyring(config_dir), KeyringProtectedFile)

    # 4. Desktop Linux with DBUS_SESSION_BUS_ADDRESS (outside container) selects KeyringDefault
    monkeypatch.setenv("DBUS_SESSION_BUS_ADDRESS", "unix:path=/run/user/1000/bus")
    if not Path("/.dockerenv").exists() and not Path("/run/.containerenv").exists():
        assert isinstance(resolve_keyring(config_dir), KeyringDefault)
