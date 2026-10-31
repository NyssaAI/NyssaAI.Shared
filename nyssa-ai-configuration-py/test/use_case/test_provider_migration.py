# Responsibility: Exercise F-201 provider migration (move) across plaintext, aesLocal, and cloud key vault providers.
# Public interface:
# +-- InMemoryVault
# |   +-- name
# |   +-- new(delete_retains)
# |   +-- read(pointer)
# |   +-- remove(pointer)
# |   `-- write(namespace, key, secret)
# +-- InMemoryVaultFactory
# |   +-- authorization(options)
# |   +-- create(options, token_source)
# |   +-- kind
# |   `-- new(vault)
# +-- test_move_between_plaintext_aes_local_and_cloud_vault(tmp_path)
# `-- test_move_reports_source_value_remains_when_vault_retains_deleted_secret(tmp_path)
"""Use-case tests for F-201 provider migration across local and cloud vault providers."""

import json
from pathlib import Path

from nyssa_ai_configuration import (
    AccessTokenSource,
    BootstrapOptions,
    CloudKeyVault,
    CloudKeyVaultRecovery,
    ConfigurationClient,
    ConfigurationPointer,
    KeyringInMemory,
    OAuthOptions,
    Result,
    VaultOptions,
    ok,
)


class InMemoryVault:
    """In-memory CloudKeyVault test double for F-201 migration workflows."""

    def __init__(self, delete_retains: bool = False) -> None:
        """Initialize with empty item store and optional soft-delete retention flag."""
        self.items: dict[str, str] = {}
        self.delete_retains = delete_retains
        self.recovery: CloudKeyVaultRecovery | None = None

    @property
    def name(self) -> str:
        """Return provider instance name."""
        return "bitwarden"

    def read(self, pointer: ConfigurationPointer) -> Result[str]:
        """Read secret by pointer value."""
        return ok(self.items[pointer.value])

    def write(self, namespace: str, key: str, secret: str) -> Result[ConfigurationPointer]:
        """Write secret and return a pointer."""
        ref = f"vault-ref:{namespace}:{key}"
        self.items[ref] = secret
        return ok(ConfigurationPointer(provider="bitwarden", value=ref))

    def remove(self, pointer: ConfigurationPointer) -> Result[bool]:
        """Remove secret or retain it when delete_retains is True."""
        if not self.delete_retains:
            self.items.pop(pointer.value, None)
        return ok(self.delete_retains)


class InMemoryVaultFactory:
    """Factory binding InMemoryVault under kind 'bitwarden'."""

    def __init__(self, vault: InMemoryVault) -> None:
        """Initialize with vault instance."""
        self._vault = vault

    @property
    def kind(self) -> str:
        """Return provider kind."""
        return "bitwarden"

    def create(self, options: VaultOptions, token_source: AccessTokenSource | None = None) -> Result[CloudKeyVault]:
        """Return the configured InMemoryVault."""
        del options, token_source
        return ok(self._vault)

    def authorization(self, options: VaultOptions) -> Result[OAuthOptions | None]:
        """Return None for OAuth options."""
        del options
        return ok(None)


def test_move_between_plaintext_aes_local_and_cloud_vault(tmp_path: Path) -> None:
    """Verify move migrates a setting from plaintext -> aesLocal -> cloud vault -> plaintext while preserving value."""
    store_dir = tmp_path / "store"
    vault = InMemoryVault()
    created = ConfigurationClient.create(
        BootstrapOptions(
            application_name="nyssa-app",
            configuration_directory=str(store_dir),
            manifest_path=str(tmp_path / "config-manifest.json"),
            key_vault=VaultOptions(name="bitwarden", kind="bitwarden"),
        ),
        keyring=KeyringInMemory(),
        vault_factories=[InMemoryVaultFactory(vault)],
    )
    assert created.ok
    client = created.value

    assert client.set("billing", "webhookSecret", "whsec_live_12345").ok

    # Move plaintext -> aesLocal
    to_aes = client.move("billing", "webhookSecret", "aesLocal")
    assert to_aes.result.ok
    assert to_aes.result.value.source_value_remains is False

    # Move aesLocal -> bitwarden
    to_vault = client.move("billing", "webhookSecret", "bitwarden")
    assert to_vault.result.ok
    assert vault.items["vault-ref:billing:webhookSecret"] == "whsec_live_12345"

    raw_doc = json.loads((store_dir / "billing.json").read_text(encoding="utf-8"))
    assert raw_doc["webhookSecret"] == {
        "value": "vault-ref:billing:webhookSecret",
        "provider": "bitwarden",
    }

    # Move bitwarden -> plaintext ("")
    to_plain = client.move("billing", "webhookSecret", "")
    assert to_plain.result.ok
    assert to_plain.result.value.source_value_remains is False
    assert "vault-ref:billing:webhookSecret" not in vault.items

    final_val = client.get("billing", "webhookSecret")
    assert final_val.ok
    assert final_val.value == "whsec_live_12345"


def test_move_reports_source_value_remains_when_vault_retains_deleted_secret(tmp_path: Path) -> None:
    """Verify move reports source_value_remains=True when the source vault retains old secrets."""
    vault = InMemoryVault(delete_retains=True)
    created = ConfigurationClient.create(
        BootstrapOptions(
            application_name="nyssa-app",
            configuration_directory=str(tmp_path / "store"),
            manifest_path=str(tmp_path / "config-manifest.json"),
            key_vault=VaultOptions(name="bitwarden", kind="bitwarden"),
        ),
        keyring=KeyringInMemory(),
        vault_factories=[InMemoryVaultFactory(vault)],
    )
    assert created.ok
    client = created.value

    assert client.set("billing", "token", "tok_999", provider="bitwarden").ok
    moved = client.move("billing", "token", "aesLocal")

    assert moved.result.ok
    assert moved.result.value.source_value_remains is True
