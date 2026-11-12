# Responsibility: End-to-end use-case tests for Bitwarden Secrets Manager (bws) integration and secret caching with ConfigurationClient.
# Public interface:
# +-- test_bitwarden_secrets_vault_caching_and_custom_cache_store(tmp_path)
# `-- test_bitwarden_secrets_vault_end_to_end_pointer_storage_and_namespace_assembly(tmp_path)
"""Use-case tests for Bitwarden Secrets Manager (bws) cloud key vault integration and caching."""

from __future__ import annotations

from collections.abc import Mapping, Sequence
import json
from pathlib import Path

from nyssa_ai_configuration import (
    MANIFEST_KEY_ACCOUNT,
    BootstrapOptions,
    CloudKeyVaultFactoryBitwarden,
    ConfigurationClient,
    ConfigurationManifestWriter,
    KeyringInMemory,
    ProtectedFileSystem,
    Result,
    VaultOptions,
    fail,
    ok,
)


def test_bitwarden_secrets_vault_end_to_end_pointer_storage_and_namespace_assembly(tmp_path: Path) -> None:
    """Verify ConfigurationClient writes pointers to Bitwarden Secrets Manager, reads secrets in 1 list call, and removes them."""
    remote_store: dict[str, tuple[str, str]] = {}
    list_calls = [0]
    get_calls = [0]

    def runner(binary: str, args: Sequence[str], env: Mapping[str, str]) -> Result[str]:
        del binary, env
        if len(args) >= 2 and args[0] == "secret" and args[1] == "list":
            list_calls[0] += 1
            payload = [
                {"id": item_id, "key": item_key, "value": item_val}
                for item_id, (item_key, item_val) in remote_store.items()
            ]
            return ok(json.dumps(payload))
        if len(args) >= 5 and args[0] == "secret" and args[1] == "create":
            secret_key, secret_val = args[2], args[3]
            new_id = f"bws-uuid-{len(remote_store) + 1}"
            remote_store[new_id] = (secret_key, secret_val)
            return ok(json.dumps({"id": new_id, "key": secret_key, "value": secret_val}))
        if len(args) >= 5 and args[0] == "secret" and args[1] == "edit":
            target_id, new_val = args[2], args[4]
            old_key, _ = remote_store[target_id]
            remote_store[target_id] = (old_key, new_val)
            return ok(json.dumps({"id": target_id, "key": old_key, "value": new_val}))
        if len(args) >= 3 and args[0] == "secret" and args[1] == "get":
            get_calls[0] += 1
            target_id = args[2]
            item_key, item_val = remote_store[target_id]
            return ok(json.dumps({"id": target_id, "key": item_key, "value": item_val}))
        if len(args) >= 3 and args[0] == "secret" and args[1] == "delete":
            target_id = args[2]
            remote_store.pop(target_id, None)
            return ok("")
        return fail("Unsupported command", "Unavailable")

    store_dir = tmp_path / "store"
    factory = CloudKeyVaultFactoryBitwarden(command_runner=runner)
    created = ConfigurationClient.create(
        BootstrapOptions(
            application_name="nyssa-bws-app",
            configuration_directory=str(store_dir),
            manifest_path=str(tmp_path / "config-manifest.json"),
            key_vault=VaultOptions(
                name="bws",
                kind="bitwarden-secrets",
                settings={"projectId": "proj-bws-99"},
                secrets={"accessToken": "bws-machine-token"},
            ),
        ),
        keyring=KeyringInMemory(),
        vault_factories=[factory],
    )
    assert created.ok
    client = created.value

    assert client.set("payments", "webhookUrl", "https://pay.nyssa.ai/hook").ok
    assert client.set("payments", "stripeApiKey", "sk_live_bws_secret", provider="bws").ok
    assert client.set("payments", "webhookSecret", "whsec_live_bws_secret", provider="bws").ok

    raw_doc = json.loads((store_dir / "payments.json").read_text(encoding="utf-8"))
    assert raw_doc["stripeApiKey"] == {
        "value": "payments/stripeApiKey",
        "provider": "bws",
    }

    lists_before_assemble = list_calls[0]
    assembled = client.get_namespace("payments", dict)
    assert assembled.ok
    assert assembled.value == {
        "webhookUrl": "https://pay.nyssa.ai/hook",
        "stripeApiKey": "sk_live_bws_secret",
        "webhookSecret": "whsec_live_bws_secret",
    }
    # Both Bitwarden secrets in the namespace are resolved from 1 'bws secret list' call and 0 'bws secret get' calls!
    assert list_calls[0] == lists_before_assemble + 1
    assert get_calls[0] == 0

    removed = client.remove("payments", "stripeApiKey")
    assert removed.ok
    assert removed.value.remote_value_remains is False
    assert client.remove("payments", "webhookSecret").ok
    assert len(remote_store) == 0


def test_bitwarden_secrets_vault_caching_and_custom_cache_store(tmp_path: Path) -> None:
    """Verify ConfigurationClient uses injected SecretCache (simulating cross-process CLI keyring cache) and respects cacheTTLSeconds."""
    remote_store: dict[str, tuple[str, str]] = {"id-1": ("payments/apiKey", "initial-secret")}
    list_calls = [0]
    now = [1000.0]

    def runner(binary: str, args: Sequence[str], env: Mapping[str, str]) -> Result[str]:
        del binary, env
        if len(args) >= 2 and args[0] == "secret" and args[1] == "list":
            list_calls[0] += 1
            payload = [
                {"id": item_id, "key": item_key, "value": item_val}
                for item_id, (item_key, item_val) in remote_store.items()
            ]
            return ok(json.dumps(payload))
        if len(args) >= 5 and args[0] == "secret" and args[1] == "edit":
            target_id, new_val = args[2], args[4]
            old_key, _ = remote_store[target_id]
            remote_store[target_id] = (old_key, new_val)
            return ok(json.dumps({"id": target_id, "key": old_key, "value": new_val}))
        return fail("Unsupported command", "Unavailable")

    class _CrossProcessKeyringCache:
        def __init__(self) -> None:
            self.entries: dict[str, tuple[str, float]] = {}

        def get(self, key: str) -> str | None:
            item = self.entries.get(key)
            if item is None or now[0] >= item[1]:
                self.entries.pop(key, None)
                return None
            return item[0]

        def set(self, key: str, value: str, ttl_seconds: int) -> None:
            self.entries[key] = (value, now[0] + float(ttl_seconds))

        def remove(self, key: str) -> None:
            self.entries.pop(key, None)

        def clear(self) -> None:
            self.entries.clear()

    shared_os_cache = _CrossProcessKeyringCache()
    store_dir = tmp_path / "store"
    manifest_path = tmp_path / "config-manifest.json"
    keyring = KeyringInMemory()
    factory = CloudKeyVaultFactoryBitwarden(command_runner=runner)

    def make_cli_client() -> ConfigurationClient:
        res = ConfigurationClient.create(
            BootstrapOptions(
                application_name="cli-app",
                configuration_directory=str(store_dir),
                manifest_path=str(manifest_path),
                key_vault=VaultOptions(
                    name="bws",
                    kind="bitwarden-secrets",
                    cache_ttl_seconds=60,
                    settings={"projectId": "proj-cli-1"},
                    secrets={"accessToken": "bws-token"},
                ),
            ),
            keyring=keyring,
            vault_factories=[factory],
            clock=lambda: now[0],
            secret_cache=shared_os_cache,
        )
        assert res.ok
        return res.value

    # Process 1 writes pointer and reads secret (populates shared_os_cache)
    cli_1 = make_cli_client()
    assert cli_1.set("payments", "apiKey", "initial-secret", provider="bws").ok
    list_calls_after_set = list_calls[0]
    r1 = cli_1.get("payments", "apiKey")
    assert r1.ok and r1.value == "initial-secret"
    assert list_calls[0] == list_calls_after_set + 1

    # Process 2 (10 seconds later) reads secret -> served from shared_os_cache without calling bws!
    now[0] += 10.0
    cli_2 = make_cli_client()
    r2 = cli_2.get("payments", "apiKey")
    assert r2.ok and r2.value == "initial-secret"
    assert list_calls[0] == list_calls_after_set + 1

    # Process 3 (after 60s TTL expires) reads secret -> calls bws again
    remote_store["id-1"] = ("payments/apiKey", "rotated-secret")
    now[0] += 55.0
    cli_3 = make_cli_client()
    r3 = cli_3.get("payments", "apiKey")
    assert r3.ok and r3.value == "rotated-secret"
    assert list_calls[0] == list_calls_after_set + 2


def test_root_manifest_multi_vault_coexistence_move_and_aad_isolation(tmp_path: Path) -> None:
    """Verify bws-corporate, bws-prod, and bws-staging coexist in one client, support move(), and reject AAD transplant or missing _manifest key."""
    projects: dict[str, dict[str, tuple[str, str]]] = {
        "proj-corporate": {},
        "proj-prod": {},
        "proj-staging": {},
    }
    tokens_by_project: dict[str, str] = {}

    def runner(binary: str, args: Sequence[str], env: Mapping[str, str]) -> Result[str]:
        del binary
        tok = env.get("BWS_ACCESS_TOKEN", "")
        if len(args) >= 3 and args[0] == "secret" and args[1] == "list":
            proj_id = args[2]
            tokens_by_project[proj_id] = tok
            payload = [
                {"id": item_id, "key": item_key, "value": item_val}
                for item_id, (item_key, item_val) in projects[proj_id].items()
            ]
            return ok(json.dumps(payload))
        if len(args) >= 5 and args[0] == "secret" and args[1] == "create":
            secret_key, secret_val, proj_id = args[2], args[3], args[4]
            tokens_by_project[proj_id] = tok
            new_id = f"{proj_id}-id-{len(projects[proj_id]) + 1}"
            projects[proj_id][new_id] = (secret_key, secret_val)
            return ok(json.dumps({"id": new_id, "key": secret_key, "value": secret_val}))
        if len(args) >= 3 and args[0] == "secret" and args[1] == "delete":
            target_id = args[2]
            for store in projects.values():
                store.pop(target_id, None)
            return ok("")
        return fail("Unsupported command", "Unavailable")

    manifest_path = tmp_path / "config-manifest.json"
    store_dir = tmp_path / "store"
    keyring = KeyringInMemory()
    writer = ConfigurationManifestWriter()

    for v_name, proj_id, token_val in (
        ("bws-corporate", "proj-corporate", "tok-corp-111"),
        ("bws-prod", "proj-prod", "tok-prod-222"),
        ("bws-staging", "proj-staging", "tok-stage-333"),
    ):
        assert writer.set_vault(
            manifest_path=manifest_path,
            vault=VaultOptions(
                name=v_name,
                kind="bitwarden-secrets",
                settings={"projectId": proj_id},
                secrets={"accessToken": token_val},
            ),
            keyring=keyring,
        ).ok

    factory = CloudKeyVaultFactoryBitwarden(command_runner=runner)
    created = ConfigurationClient.create(
        BootstrapOptions(
            application_name="multi-vault-app",
            configuration_directory=str(store_dir),
            manifest_path=str(manifest_path),
        ),
        keyring=keyring,
        vault_factories=[factory],
    )
    assert created.ok
    client = created.value

    assert client.set("shared", "smtpPass", "corp-smtp-secret", provider="bws-corporate").ok
    assert client.set("payments", "stripeKey", "prod-stripe-secret", provider="bws-prod").ok
    assert client.set("payments", "sandboxKey", "staging-stripe-secret", provider="bws-staging").ok

    assert tokens_by_project == {
        "proj-corporate": "tok-corp-111",
        "proj-prod": "tok-prod-222",
        "proj-staging": "tok-stage-333",
    }
    smtp_res = client.get("shared", "smtpPass")
    assert smtp_res.ok and smtp_res.value == "corp-smtp-secret"
    ns_res = client.get_namespace("payments", dict)
    assert ns_res.ok and ns_res.value == {
        "stripeKey": "prod-stripe-secret",
        "sandboxKey": "staging-stripe-secret",
    }

    # Migrate a secret from bws-staging -> bws-prod
    moved = client.move("payments", "sandboxKey", "bws-prod")
    assert moved.result.ok
    assert moved.result.value.source_value_remains is False
    raw_payments = json.loads((store_dir / "payments.json").read_text(encoding="utf-8"))
    assert raw_payments["sandboxKey"]["provider"] == "bws-prod"
    moved_val = client.get("payments", "sandboxKey")
    assert moved_val.ok and moved_val.value == "staging-stripe-secret"

    # Missing _manifest key fails closed (read-only get_existing_key never auto-creates _manifest!)
    empty_manifest_kr = KeyringInMemory()
    missing_key_res = ConfigurationClient.create(
        BootstrapOptions(
            application_name="multi-vault-app",
            configuration_directory=str(store_dir),
            manifest_path=str(manifest_path),
        ),
        keyring=keyring,
        manifest_keyring=empty_manifest_kr,
        vault_factories=[factory],
    )
    assert not missing_key_res.ok
    assert missing_key_res.category == "Validation"
    assert (
        missing_key_res.message
        == "configuration: manifest encryption key 'nyssa-ai-configuration/_manifest' was not found in the keyring."
    )
    empty_check = empty_manifest_kr.get_key("nyssa-ai-configuration", MANIFEST_KEY_ACCOUNT)
    assert empty_check.ok and empty_check.value is None

    # AAD transplant across bws-staging -> bws-prod is rejected at unseal!
    raw_manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    staging_env = raw_manifest["keyVaults"]["bws-staging"]["secrets"]["accessToken"]
    raw_manifest["keyVaults"]["bws-prod"]["secrets"]["accessToken"] = staging_env
    assert ProtectedFileSystem.create_file(manifest_path, json.dumps(raw_manifest, indent=2)).ok

    tampered_res = ConfigurationClient.create(
        BootstrapOptions(
            application_name="multi-vault-app",
            configuration_directory=str(store_dir),
            manifest_path=str(manifest_path),
        ),
        keyring=keyring,
        vault_factories=[factory],
    )
    assert not tampered_res.ok
    assert tampered_res.category == "Validation"
    assert "keyVaults.bws-prod.secrets.accessToken" in tampered_res.message
    assert "tok-stage-333" not in tampered_res.message
