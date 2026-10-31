# Responsibility: Exercise F-102 keyed DTO serialization, decomposed namespace replace/merge, and legacy namespaceSettings migration.
# Public interface:
# +-- EndpointConfig
# |   +-- host
# |   +-- port
# |   `-- tls
# +-- test_keyed_dto_and_literal_quotes_round_trip(tmp_path)
# +-- test_legacy_namespace_settings_read_merge_and_replace_migration(tmp_path)
# `-- test_set_namespace_and_merge_settings_preserve_existing_key_providers(tmp_path)
"""Use-case tests for F-102 structured settings, namespace decomposition, and legacy compatibility."""

from dataclasses import dataclass
import json
from pathlib import Path

from nyssa_ai_configuration import (
    BootstrapOptions,
    ConfigurationClient,
    KeyringInMemory,
    ProtectedFileSystem,
)


@dataclass(frozen=True, slots=True)
class EndpointConfig:
    """Sample typed configuration DTO."""

    host: str
    port: int
    tls: bool


def test_keyed_dto_and_literal_quotes_round_trip(tmp_path: Path) -> None:
    """Verify set_as/get_as round-trips dataclasses and preserves literal quotes on get_as(..., str)."""
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

    dto = EndpointConfig(host="db.nyssa.internal", port=5432, tls=True)
    assert client.set_as("Connections", "primaryDb", dto).ok

    loaded = client.get_as("Connections", "primaryDb", EndpointConfig)
    assert loaded.ok
    assert loaded.value == dto

    # Literal quotes on a string stored via set() must be preserved when read via get_as(..., str)
    assert client.set("Connections", "quotedLiteral", '"already-quoted"').ok
    str_loaded = client.get_as("Connections", "quotedLiteral", str)
    assert str_loaded.ok
    assert str_loaded.value == '"already-quoted"'


def test_set_namespace_and_merge_settings_preserve_existing_key_providers(tmp_path: Path) -> None:
    """Verify decomposed set_namespace and merge_settings preserve aesLocal on existing keys and remove omitted keys on replace."""
    store_dir = tmp_path / "store"
    created = ConfigurationClient.create(
        BootstrapOptions(
            application_name="nyssa-app",
            configuration_directory=str(store_dir),
            manifest_path=str(tmp_path / "config-manifest.json"),
        ),
        keyring=KeyringInMemory(),
    )
    assert created.ok
    client = created.value

    assert client.set("payment", "apiKey", "initial-secret", provider="aesLocal").ok
    assert client.set("payment", "obsoleteKey", "remove-me").ok

    replace_attempt = client.set_namespace(
        "payment",
        {"apiKey": "rotated-secret", "timeoutMs": 3000, "nested": {"retries": 3}},
    )
    assert replace_attempt.result.ok

    # Verify obsoleteKey was removed and apiKey remained encrypted under aesLocal
    raw_doc = json.loads((store_dir / "payment.json").read_text(encoding="utf-8"))
    assert "obsoleteKey" not in raw_doc
    assert raw_doc["apiKey"]["provider"] == "aesLocal"
    assert raw_doc["apiKey"]["encrypted"] is True

    merge_attempt = client.merge_settings("payment", {"timeoutMs": 4500, "region": "us-east"})
    assert merge_attempt.result.ok

    assembled = client.get_namespace("payment", dict)
    assert assembled.ok
    assert assembled.value == {
        "apiKey": "rotated-secret",
        "timeoutMs": 4500,
        "nested": {"retries": 3},
        "region": "us-east",
    }


def test_legacy_namespace_settings_read_merge_and_replace_migration(tmp_path: Path) -> None:
    """Verify legacy 'namespaceSettings' whole-object entries are read, expanded on merge, and removed on replace."""
    store_dir = tmp_path / "store"
    created = ConfigurationClient.create(
        BootstrapOptions(
            application_name="nyssa-app",
            configuration_directory=str(store_dir),
            manifest_path=str(tmp_path / "config-manifest.json"),
        ),
        keyring=KeyringInMemory(),
    )
    assert created.ok
    client = created.value

    legacy_payload = {
        "namespaceSettings": {
            "value": {"host": "legacy.nyssa.local", "port": 8080},
        },
        "port": {
            "value": 9090,
        },
    }
    assert ProtectedFileSystem.create_file(store_dir / "legacy.json", json.dumps(legacy_payload, indent=2)).ok

    # Explicit decomposed key 'port' overrides legacy 'namespaceSettings' property 'port'
    read_legacy = client.get_namespace("legacy", dict)
    assert read_legacy.ok
    assert read_legacy.value == {"host": "legacy.nyssa.local", "port": 9090}

    # merge_settings expands legacy namespaceSettings into top-level decomposed keys and deletes namespaceSettings
    merge_res = client.merge_settings("legacy", {"tls": True})
    assert merge_res.result.ok

    raw_after_merge = json.loads((store_dir / "legacy.json").read_text(encoding="utf-8"))
    assert "namespaceSettings" not in raw_after_merge
    assert set(raw_after_merge.keys()) == {"host", "port", "tls"}

    read_after_merge = client.get_namespace("legacy", EndpointConfig)
    assert read_after_merge.ok
    assert read_after_merge.value == EndpointConfig(host="legacy.nyssa.local", port=9090, tls=True)
