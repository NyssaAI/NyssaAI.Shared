# Responsibility: Verify KeyringSystemdCreds credential discovery, base64/binary key parsing, immutability, and bootstrap selection.
# Public interface:
# +-- test_configuration_bootstrap_selects_keyring_systemd_creds_when_credentials_directory_set(tmp_path, monkeypatch)
# +-- test_keyring_systemd_creds_explicit_credential_name_resolves_named_and_key_suffixed_files(tmp_path, monkeypatch)
# +-- test_keyring_systemd_creds_get_base64_key_reads_and_parses_32_byte_key_with_whitespace(tmp_path, monkeypatch)
# +-- test_keyring_systemd_creds_get_invalid_key_material_returns_validation_error(tmp_path, monkeypatch, label, payload)
# +-- test_keyring_systemd_creds_get_missing_file_returns_not_found(tmp_path, monkeypatch)
# +-- test_keyring_systemd_creds_get_raw_binary_key_reads_32_byte_binary_material(tmp_path, monkeypatch)
# +-- test_keyring_systemd_creds_get_single_fallback_file_uses_only_credential_file(tmp_path, monkeypatch)
# +-- test_keyring_systemd_creds_is_available_reports_presence_and_absence_of_credentials_directory(tmp_path, monkeypatch)
# +-- test_keyring_systemd_creds_rejects_invalid_service_or_account_addresses(tmp_path, monkeypatch)
# `-- test_keyring_systemd_creds_set_and_delete_return_forbidden_error(tmp_path, monkeypatch)
"""Unit tests for KeyringSystemdCreds and systemd-creds bootstrap resolution."""

import base64
import os
from pathlib import Path

import pytest

from nyssa_ai_configuration import (
    BootstrapOptions,
    ConfigurationClient,
    KeyringProtectedFile,
    KeyringSystemdCreds,
    resolve_keyring,
)


def test_keyring_systemd_creds_is_available_reports_presence_and_absence_of_credentials_directory(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    """Verify KeyringSystemdCreds.is_available() checks CREDENTIALS_DIRECTORY presence and directory existence."""
    monkeypatch.delenv("CREDENTIALS_DIRECTORY", raising=False)
    keyring = KeyringSystemdCreds()
    assert KeyringSystemdCreds.is_available() is False
    assert keyring.is_available() is False

    monkeypatch.setenv("CREDENTIALS_DIRECTORY", str(tmp_path / "missing-dir"))
    assert KeyringSystemdCreds.is_available() is False
    assert keyring.is_available() is False

    creds_dir = tmp_path / "credentials"
    creds_dir.mkdir(parents=True, exist_ok=True)
    monkeypatch.setenv("CREDENTIALS_DIRECTORY", str(creds_dir))
    assert KeyringSystemdCreds.is_available() is True
    assert keyring.is_available() is True


def test_keyring_systemd_creds_get_base64_key_reads_and_parses_32_byte_key_with_whitespace(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    """Verify KeyringSystemdCreds reads and trims 32-byte base64 key material from <account>.key."""
    creds_dir = tmp_path / "credentials"
    creds_dir.mkdir(parents=True, exist_ok=True)
    monkeypatch.setenv("CREDENTIALS_DIRECTORY", str(creds_dir))

    expected_key = os.urandom(32)
    b64_text = f"  {base64.b64encode(expected_key).decode('ascii')}\n\t "
    (creds_dir / "OrdersService.key").write_text(b64_text, encoding="utf-8")

    keyring = KeyringSystemdCreds()
    res = keyring.get_key("nyssa-ai-configuration", "OrdersService")

    assert res.ok
    assert res.value == expected_key


def test_keyring_systemd_creds_get_raw_binary_key_reads_32_byte_binary_material(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    """Verify KeyringSystemdCreds reads raw 32-byte binary key material from <service>.key."""
    creds_dir = tmp_path / "credentials"
    creds_dir.mkdir(parents=True, exist_ok=True)
    monkeypatch.setenv("CREDENTIALS_DIRECTORY", str(creds_dir))

    expected_key = bytes(range(32))
    (creds_dir / "nyssa-ai-configuration.key").write_bytes(expected_key)

    keyring = KeyringSystemdCreds()
    res = keyring.get_key("nyssa-ai-configuration", "OrdersService")

    assert res.ok
    assert res.value == expected_key


def test_keyring_systemd_creds_get_single_fallback_file_uses_only_credential_file(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    """Verify KeyringSystemdCreds falls back to the single credential file when only one file exists."""
    creds_dir = tmp_path / "credentials"
    creds_dir.mkdir(parents=True, exist_ok=True)
    monkeypatch.setenv("CREDENTIALS_DIRECTORY", str(creds_dir))

    expected_key = os.urandom(32)
    (creds_dir / "systemd-provisioned-secret").write_text(
        base64.b64encode(expected_key).decode("ascii"), encoding="utf-8"
    )

    keyring = KeyringSystemdCreds()
    res = keyring.get_key("nyssa-ai-configuration", "HeadlessWorker")

    assert res.ok
    assert res.value == expected_key


def test_keyring_systemd_creds_explicit_credential_name_resolves_named_and_key_suffixed_files(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    """Verify explicit systemd_credential_name resolves <name> or <name>.key and does not fall back to unrelated files."""
    creds_dir = tmp_path / "credentials"
    creds_dir.mkdir(parents=True, exist_ok=True)
    monkeypatch.setenv("CREDENTIALS_DIRECTORY", str(creds_dir))

    expected_key = os.urandom(32)
    other_key = os.urandom(32)
    (creds_dir / "unrelated-single-file").write_bytes(other_key)

    keyring = KeyringSystemdCreds("custom-cred")
    missing_res = keyring.get_key("nyssa-ai-configuration", "OrdersService")
    assert not missing_res.ok
    assert missing_res.category == "NotFound"

    (creds_dir / "custom-cred.key").write_bytes(expected_key)
    found_res = keyring.get_key("nyssa-ai-configuration", "OrdersService")
    assert found_res.ok
    assert found_res.value == expected_key


def test_keyring_systemd_creds_get_missing_file_returns_not_found(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    """Verify KeyringSystemdCreds returns NotFound when CREDENTIALS_DIRECTORY is unset, missing, or empty."""
    monkeypatch.delenv("CREDENTIALS_DIRECTORY", raising=False)
    keyring = KeyringSystemdCreds()

    unset_res = keyring.get_key("nyssa-ai-configuration", "MissingApp")
    assert not unset_res.ok
    assert unset_res.category == "NotFound"

    creds_dir = tmp_path / "credentials"
    creds_dir.mkdir(parents=True, exist_ok=True)
    monkeypatch.setenv("CREDENTIALS_DIRECTORY", str(creds_dir))

    empty_dir_res = keyring.get_key("nyssa-ai-configuration", "MissingApp")
    assert not empty_dir_res.ok
    assert empty_dir_res.category == "NotFound"


@pytest.mark.parametrize(
    ("label", "payload"),
    [
        ("empty_file", b""),
        ("whitespace_only", b"   \n\t  "),
        ("malformed_base64", b"not-valid-base64!!!@@@###$$$"),
        ("invalid_utf8_bytes", b"\xff\xfe\xfd\xfc\xfb"),
        ("16_byte_base64_too_short", base64.b64encode(b"\x11" * 16)),
        ("64_byte_base64_too_long", base64.b64encode(b"\x22" * 64)),
    ],
)
def test_keyring_systemd_creds_get_invalid_key_material_returns_validation_error(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    label: str,
    payload: bytes,
) -> None:
    """Verify KeyringSystemdCreds returns Validation on empty, malformed base64, or non-32-byte key material."""
    del label
    creds_dir = tmp_path / "credentials"
    creds_dir.mkdir(parents=True, exist_ok=True)
    monkeypatch.setenv("CREDENTIALS_DIRECTORY", str(creds_dir))
    (creds_dir / "OrdersService.key").write_bytes(payload)

    keyring = KeyringSystemdCreds()
    res = keyring.get_key("nyssa-ai-configuration", "OrdersService")

    assert not res.ok
    assert res.category == "Validation"


def test_keyring_systemd_creds_rejects_invalid_service_or_account_addresses(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    """Verify KeyringSystemdCreds rejects blank or control-character service/account names with Validation."""
    creds_dir = tmp_path / "credentials"
    creds_dir.mkdir(parents=True, exist_ok=True)
    monkeypatch.setenv("CREDENTIALS_DIRECTORY", str(creds_dir))
    keyring = KeyringSystemdCreds()

    blank_res = keyring.get_key("   ", "OrdersService")
    assert not blank_res.ok
    assert blank_res.category == "Validation"

    ctrl_res = keyring.get_key("nyssa-ai-configuration", "Orders\x00Service")
    assert not ctrl_res.ok
    assert ctrl_res.category == "Validation"


def test_keyring_systemd_creds_set_and_delete_return_forbidden_error(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    """Verify KeyringSystemdCreds rejects set_key and delete_key with Forbidden."""
    creds_dir = tmp_path / "credentials"
    creds_dir.mkdir(parents=True, exist_ok=True)
    monkeypatch.setenv("CREDENTIALS_DIRECTORY", str(creds_dir))
    keyring = KeyringSystemdCreds()

    set_res = keyring.set_key("nyssa-ai-configuration", "OrdersService", os.urandom(32))
    assert not set_res.ok
    assert set_res.category == "Forbidden"
    assert set_res.message == KeyringSystemdCreds.IMMUTABLE_ERROR_MESSAGE

    del_res = keyring.delete_key("nyssa-ai-configuration", "OrdersService")
    assert not del_res.ok
    assert del_res.category == "Forbidden"
    assert del_res.message == KeyringSystemdCreds.IMMUTABLE_ERROR_MESSAGE


def test_configuration_bootstrap_selects_keyring_systemd_creds_when_credentials_directory_set(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    """Verify ConfigurationClient.create and resolve_keyring select KeyringSystemdCreds when CREDENTIALS_DIRECTORY is set."""
    creds_dir = tmp_path / "credentials"
    creds_dir.mkdir(parents=True, exist_ok=True)
    monkeypatch.setenv("CREDENTIALS_DIRECTORY", str(creds_dir))
    monkeypatch.delenv("NYSSA_CONFIG_DIR", raising=False)

    master_key = os.urandom(32)
    (creds_dir / "custom-systemd-cred").write_text(
        base64.b64encode(master_key).decode("ascii"), encoding="utf-8"
    )

    manifest_path = tmp_path / "config-manifest.json"
    config_dir = tmp_path / "config"
    opts = BootstrapOptions(
        application_name="SystemdServiceApp",
        manifest_path=str(manifest_path),
        configuration_directory=str(config_dir),
        systemd_credential_name="custom-systemd-cred",
    )

    resolved = resolve_keyring(config_dir, opts)
    assert isinstance(resolved, KeyringSystemdCreds)

    client_res = ConfigurationClient.create(opts)
    assert client_res.ok
    client = client_res.value

    assert not (config_dir / ".nyssa-key").exists()

    set_res = client.set("database", "password", "systemd-protected-secret", provider="aesLocal")
    assert set_res.ok

    get_res = client.get("database", "password")
    assert get_res.ok
    assert get_res.value == "systemd-protected-secret"

    # Also verify fallback to KeyringProtectedFile when CREDENTIALS_DIRECTORY is unset and .nyssa-key exists
    monkeypatch.delenv("CREDENTIALS_DIRECTORY", raising=False)
    protected_dir = tmp_path / "config-protected"
    protected_dir.mkdir(parents=True, exist_ok=True)
    (protected_dir / ".nyssa-key").write_text(
        base64.b64encode(os.urandom(32)).decode("ascii"), encoding="utf-8"
    )
    fallback_keyring = resolve_keyring(protected_dir)
    assert isinstance(fallback_keyring, KeyringProtectedFile)
