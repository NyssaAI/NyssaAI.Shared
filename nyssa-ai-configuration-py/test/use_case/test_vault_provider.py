# Responsibility: Exercise F-203 cloud key vault pointer storage, multi-provider namespace assembly, OAuth credential wiring, and CircularCredential prevention.
# Public interface:
# +-- FakeCloudVault
# |   +-- name
# |   +-- new(token_source)
# |   +-- read(pointer)
# |   +-- remove(pointer)
# |   `-- write(namespace, key, secret)
# +-- FakeCloudVaultFactory
# |   +-- authorization(options)
# |   +-- create(options, token_source)
# |   +-- kind
# |   `-- new()
# +-- test_circular_credential_is_blocked_on_both_write_and_read(tmp_path)
# `-- test_cloud_vault_pointer_storage_and_multi_provider_namespace_assembly(tmp_path)
"""Use-case tests for F-203 cloud key vault integration, OAuth token wiring, and circular credential protection."""

import json
from pathlib import Path

from nyssa_ai_configuration import (
    AccessTokenSource,
    BootstrapOptions,
    CloudKeyVault,
    CloudKeyVaultRecovery,
    ConfigurationClient,
    ConfigurationManifestWriter,
    ConfigurationPointer,
    KeyringInMemory,
    OAuthOptions,
    ProtectedFileSystem,
    Result,
    VaultOptions,
    fail,
    ok,
)


class FakeCloudVault:
    """CloudKeyVault implementation that authenticates via AccessTokenSource when provided."""

    def __init__(self, token_source: AccessTokenSource | None = None) -> None:
        """Initialize with optional AccessTokenSource."""
        self._token_source = token_source
        self.store: dict[str, str] = {}
        self.last_access_token: str | None = None
        self.recovery: CloudKeyVaultRecovery | None = None

    @property
    def name(self) -> str:
        """Return provider instance name."""
        return "bitwarden"

    def _authenticate(self) -> Result[None]:
        if self._token_source is None:
            return ok(None)
        tok = self._token_source.get_access_token()
        if not tok.ok:
            return tok
        self.last_access_token = tok.value
        return ok(None)

    def read(self, pointer: ConfigurationPointer) -> Result[str]:
        """Authenticate and read secret by pointer."""
        auth = self._authenticate()
        if not auth.ok:
            return auth
        if pointer.value not in self.store:
            return fail("Vault item not found.", "NotFound")
        return ok(self.store[pointer.value])

    def write(self, namespace: str, key: str, secret: str) -> Result[ConfigurationPointer]:
        """Authenticate, store secret, and return a ConfigurationPointer."""
        auth = self._authenticate()
        if not auth.ok:
            return auth
        item_id = f"bw://{namespace}/{key}"
        self.store[item_id] = secret
        return ok(ConfigurationPointer(provider="bitwarden", value=item_id))

    def remove(self, pointer: ConfigurationPointer) -> Result[bool]:
        """Authenticate and remove item by pointer."""
        auth = self._authenticate()
        if not auth.ok:
            return auth
        self.store.pop(pointer.value, None)
        return ok(False)


class FakeCloudVaultFactory:
    """Factory creating FakeCloudVault instances for kind 'bitwarden'."""

    def __init__(self) -> None:
        """Initialize factory."""
        self.last_vault: FakeCloudVault | None = None

    @property
    def kind(self) -> str:
        """Return provider kind."""
        return "bitwarden"

    def create(self, options: VaultOptions, token_source: AccessTokenSource | None = None) -> Result[CloudKeyVault]:
        """Construct a FakeCloudVault wired to token_source."""
        del options
        vault = FakeCloudVault(token_source)
        self.last_vault = vault
        return ok(vault)

    def authorization(self, options: VaultOptions) -> Result[OAuthOptions | None]:
        """Return None for OAuth options."""
        del options
        return ok(None)


def test_cloud_vault_pointer_storage_and_multi_provider_namespace_assembly(tmp_path: Path) -> None:
    """Verify cloud vault secrets store only an unencrypted pointer locally and assemble cleanly with local settings."""
    store_dir = tmp_path / "store"
    factory = FakeCloudVaultFactory()
    created = ConfigurationClient.create(
        BootstrapOptions(
            application_name="nyssa-app",
            configuration_directory=str(store_dir),
            manifest_path=str(tmp_path / "config-manifest.json"),
            key_vault=VaultOptions(name="bitwarden", kind="bitwarden"),
        ),
        keyring=KeyringInMemory(),
        vault_factories=[factory],
    )
    assert created.ok
    client = created.value

    assert client.set("api", "baseUrl", "https://api.nyssa.ai").ok
    assert client.set("api", "signingSecret", "local-aes-secret", provider="aesLocal").ok
    assert client.set("api", "vaultApiKey", "cloud-vault-secret-xyz", provider="bitwarden").ok

    raw_doc = json.loads((store_dir / "api.json").read_text(encoding="utf-8"))
    assert raw_doc["vaultApiKey"] == {
        "value": "bw://api/vaultApiKey",
        "provider": "bitwarden",
    }
    assert "encrypted" not in raw_doc["vaultApiKey"]

    assembled = client.get_namespace("api", dict)
    assert assembled.ok
    assert assembled.value == {
        "baseUrl": "https://api.nyssa.ai",
        "signingSecret": "local-aes-secret",
        "vaultApiKey": "cloud-vault-secret-xyz",
    }


def test_circular_credential_is_blocked_on_both_write_and_read(tmp_path: Path) -> None:
    """Verify a vault credential entry cannot be stored in or resolved from its own cloud vault (CircularCredential)."""
    store_dir = tmp_path / "store"
    factory = FakeCloudVaultFactory()
    created = ConfigurationClient.create(
        BootstrapOptions(
            application_name="nyssa-app",
            configuration_directory=str(store_dir),
            manifest_path=str(tmp_path / "config-manifest.json"),
            key_vault=VaultOptions(
                name="bitwarden",
                kind="bitwarden",
                credential="oauth://auth/vaultToken",
            ),
        ),
        keyring=KeyringInMemory(),
        vault_factories=[factory],
    )
    assert created.ok
    client = created.value

    # Attempting to write the vault's own credential entry into the vault must fail with Conflict
    write_circular = client.set("auth", "vaultToken", "self-ref", provider="bitwarden")
    assert not write_circular.ok
    assert write_circular.category == "Conflict"

    # If a corrupted document points auth/vaultToken at bitwarden, reading any bitwarden secret must fail with Conflict
    corrupted = {
        "vaultToken": {"value": "bw://auth/vaultToken", "provider": "bitwarden"},
    }
    assert ProtectedFileSystem.create_file(store_dir / "auth.json", json.dumps(corrupted, indent=2)).ok

    read_circular = client.get("auth", "vaultToken")
    assert not read_circular.ok
    assert read_circular.category == "Conflict"


def test_manifest_vault_without_secrets_skips_manifest_keyring_and_layers_over_programmatic_vaults(
    tmp_path: Path,
) -> None:
    """Verify 0-secret manifest vaults never query _manifest keyring, replace same-named code vaults whole, and inherit code TTL when omitted."""
    manifest_path = tmp_path / "config-manifest.json"
    store_dir = tmp_path / "store"
    writer = ConfigurationManifestWriter()

    # Write a root vault with OAuth credential and NO static secrets (failing keyring proves _manifest is never touched)
    failing_manifest_kr = KeyringInMemory(fail_on_read=True, fail_on_write=True)
    set_res = writer.set_vault(
        manifest_path=manifest_path,
        vault=VaultOptions(
            name="bitwarden",
            kind="bitwarden",
            endpoint="https://manifest-vault.example",
            credential="oauth://auth/vaultToken",
            settings={"manifestOnly": "true"},
        ),
        keyring=failing_manifest_kr,
    )
    assert set_res.ok

    seen_options: list[VaultOptions] = []

    class _InspectingFactory(FakeCloudVaultFactory):
        def create(self, options: VaultOptions, token_source: AccessTokenSource | None = None) -> Result[CloudKeyVault]:
            seen_options.append(options)
            return super().create(options, token_source)

    factory = _InspectingFactory()
    created = ConfigurationClient.create(
        BootstrapOptions(
            application_name="nyssa-app",
            configuration_directory=str(store_dir),
            manifest_path=str(manifest_path),
            key_vault=VaultOptions(
                name="bitwarden",
                kind="bitwarden",
                endpoint="https://code-default.example",
                settings={"codeOnly": "should-not-bleed"},
                cache_ttl_seconds=45,
            ),
            key_vaults=[
                VaultOptions(
                    name="bitwarden-secondary",
                    kind="bitwarden",
                    endpoint="https://secondary.example",
                ),
            ],
        ),
        keyring=KeyringInMemory(),
        manifest_keyring=failing_manifest_kr,
        vault_factories=[factory],
    )
    assert created.ok
    by_name = {opt.name: opt for opt in seen_options}
    assert set(by_name.keys()) == {"bitwarden", "bitwarden-secondary"}
    # Manifest replaces 'bitwarden' whole, inheriting only cache_ttl_seconds=45 because manifest omitted cacheTTLSeconds
    assert by_name["bitwarden"].endpoint == "https://manifest-vault.example"
    assert by_name["bitwarden"].settings == {"manifestOnly": "true"}
    assert by_name["bitwarden"].cache_ttl_seconds == 45
    assert by_name["bitwarden-secondary"].endpoint == "https://secondary.example"

    # Duplicate programmatic vault names fail closed with Validation
    dup = ConfigurationClient.create(
        BootstrapOptions(
            application_name="nyssa-app",
            configuration_directory=str(store_dir),
            manifest_path=str(manifest_path),
            key_vault=VaultOptions(name="dup-vault", kind="bitwarden"),
            key_vaults=[VaultOptions(name="dup-vault", kind="bitwarden")],
        ),
        keyring=KeyringInMemory(),
        vault_factories=[factory],
    )
    assert not dup.ok
    assert dup.category == "Validation"
