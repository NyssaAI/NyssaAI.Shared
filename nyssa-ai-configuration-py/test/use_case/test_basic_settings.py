# Responsibility: Exercise F-001 bootstrap, F-101 plaintext CRUD and UTF-16 key ordering, and F-103 aesLocal encryption through ConfigurationClient.
# Public interface:
# +-- test_aes_local_encrypts_on_disk_preserves_provider_and_never_logs_canary_secret(tmp_path)
# +-- test_default_bootstrap_provisions_manifest_and_keyring_matching_platform_spec(tmp_path)
# +-- test_list_keys_sorts_by_utf16_code_unit_ordinal_order(tmp_path)
# `-- test_plaintext_crud_persists_across_fresh_client_and_rejects_provider_change(tmp_path)
"""Use-case tests for F-001 bootstrap, F-101 plaintext settings, and F-103 local AES-256-GCM secrets."""

import json
from pathlib import Path

from nyssa_ai_configuration import (
    BootstrapOptions,
    ConfigurationClient,
    ConfigurationLog,
    KeyringInMemory,
)


def test_default_bootstrap_provisions_manifest_and_keyring_matching_platform_spec(tmp_path: Path) -> None:
    """Verify default bootstrap provisions ~/.nyssa-ai/<app> and files key under nyssa-ai-configuration/<app>."""
    keyring = KeyringInMemory()
    created = ConfigurationClient.create(
        BootstrapOptions(
            application_name="AcmeCommerce",
            home_directory=str(tmp_path),
        ),
        keyring=keyring,
    )
    assert created.ok
    client = created.value

    expected_dir = (tmp_path / ".nyssa-ai" / "AcmeCommerce").resolve()
    assert Path(client.configuration_directory) == expected_dir
    assert expected_dir.is_dir()

    manifest_path = tmp_path / ".nyssa-ai" / "config-manifest.json"
    assert manifest_path.is_file()
    manifest_json = json.loads(manifest_path.read_text(encoding="utf-8"))
    assert manifest_json == {
        "version": 1,
        "applications": {
            "AcmeCommerce": {
                "configurationDirectory": str(expected_dir),
                "lockTimeoutMs": 10000,
            },
        },
    }

    stored_key = keyring.get_key("nyssa-ai-configuration", "AcmeCommerce")
    assert stored_key.ok
    assert stored_key.value is not None
    assert len(stored_key.value) == 32


def test_plaintext_crud_persists_across_fresh_client_and_rejects_provider_change(tmp_path: Path) -> None:
    """Verify plaintext CRUD persists to disk, is observable by a fresh client, and enforces provider conflict rules."""
    manifest_path = str(tmp_path / "config-manifest.json")
    store_dir = str(tmp_path / "store")
    keyring = KeyringInMemory()

    created = ConfigurationClient.create(
        BootstrapOptions(
            application_name="nyssa-app",
            configuration_directory=store_dir,
            manifest_path=manifest_path,
            lock_timeout_ms=5000,
        ),
        keyring=keyring,
    )
    assert created.ok
    client = created.value

    set_res = client.set("service", "endpoint", "https://api.nyssa.local")
    assert set_res.ok

    conflict_res = client.set("service", "endpoint", "secret-val", provider="aesLocal")
    assert not conflict_res.ok
    assert conflict_res.category == "Conflict"

    # Fresh observer client reading from the provisioned manifest
    observer_res = ConfigurationClient.create(
        BootstrapOptions(application_name="nyssa-app", manifest_path=manifest_path),
        keyring=keyring,
    )
    assert observer_res.ok
    observer = observer_res.value

    get_res = observer.get("service", "endpoint")
    assert get_res.ok
    assert get_res.value == "https://api.nyssa.local"

    rem_res = observer.remove("service", "endpoint")
    assert rem_res.ok
    assert rem_res.value.remote_value_remains is False

    missing_res = client.get("service", "endpoint")
    assert not missing_res.ok
    assert missing_res.category == "NotFound"


def test_aes_local_encrypts_on_disk_preserves_provider_and_never_logs_canary_secret(tmp_path: Path) -> None:
    """Verify aesLocal seals values on disk, preserves provider on subsequent updates, and redacts secrets from logs."""
    manifest_path = str(tmp_path / "config-manifest.json")
    store_dir = tmp_path / "store"
    keyring = KeyringInMemory()
    log_lines: list[str] = []
    logger = ConfigurationLog(sink=log_lines.append, debug_enabled=True)
    canary = "CANARY-SUPER-SECRET-998877"

    created = ConfigurationClient.create(
        BootstrapOptions(
            application_name="nyssa-app",
            configuration_directory=str(store_dir),
            manifest_path=manifest_path,
        ),
        keyring=keyring,
        logger=logger,
    )
    assert created.ok
    client = created.value

    assert client.set("database", "password", canary, provider="aesLocal").ok

    raw_doc = (store_dir / "database.json").read_text(encoding="utf-8")
    assert canary not in raw_doc
    parsed_doc = json.loads(raw_doc)
    assert parsed_doc["password"]["provider"] == "aesLocal"
    assert parsed_doc["password"]["encrypted"] is True

    # Updating without specifying provider preserves aesLocal
    updated_canary = "CANARY-ROTATED-SECRET-112233"
    assert client.set("database", "password", updated_canary).ok

    observer_res = ConfigurationClient.create(
        BootstrapOptions(application_name="nyssa-app", manifest_path=manifest_path),
        keyring=keyring,
        logger=logger,
    )
    assert observer_res.ok
    read_back = observer_res.value.get("database", "password")
    assert read_back.ok
    assert read_back.value == updated_canary

    joined_logs = "\n".join(log_lines)
    assert canary not in joined_logs
    assert updated_canary not in joined_logs
    assert any(json.loads(line)["operation"] == "store.set" for line in log_lines)


def test_list_keys_sorts_by_utf16_code_unit_ordinal_order(tmp_path: Path) -> None:
    """Verify list_keys uses UTF-16 code-unit ordinal order matching .NET StringComparer.Ordinal and JS."""
    created = ConfigurationClient.create(
        BootstrapOptions(
            application_name="nyssa-app",
            configuration_directory=str(tmp_path / "store"),
            manifest_path=str(tmp_path / "config-manifest.json"),
        ),
        keyring=KeyringInMemory(),
    )
    assert created.ok
    client = created.value

    # In UTF-8 / Unicode code point order: U+E000 (57344) < U+1F600 (128512).
    # In UTF-16 code units: U+1F600 starts with surrogate 0xD83D (55357) < 0xE000 (57344).
    bmp_private_use = "\uE000"
    supplementary_emoji = "\U0001F600"

    assert client.set("sorting", bmp_private_use, "bmp").ok
    assert client.set("sorting", supplementary_emoji, "Astral").ok
    assert client.set("sorting", "alpha", "first").ok

    keys_res = client.list_keys("sorting")
    assert keys_res.ok
    assert keys_res.value == ["alpha", supplementary_emoji, bmp_private_use]
