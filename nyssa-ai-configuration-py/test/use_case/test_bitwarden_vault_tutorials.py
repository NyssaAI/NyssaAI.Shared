# Responsibility: End-to-end use-case tests for Bitwarden Secrets Manager (bws) integration with ConfigurationClient.
# Public interface:
# `-- test_bitwarden_secrets_vault_end_to_end_pointer_storage_and_namespace_assembly(tmp_path)
"""Use-case tests for Bitwarden Secrets Manager (bws) cloud key vault integration."""

from __future__ import annotations

import json
from collections.abc import Mapping, Sequence
from pathlib import Path

from nyssa_ai_configuration import (
    BootstrapOptions,
    CloudKeyVaultFactoryBitwarden,
    ConfigurationClient,
    KeyringInMemory,
    Result,
    VaultOptions,
    fail,
    ok,
)


def test_bitwarden_secrets_vault_end_to_end_pointer_storage_and_namespace_assembly(tmp_path: Path) -> None:
    """Verify ConfigurationClient writes pointers to Bitwarden Secrets Manager, reads secrets, and removes them."""
    remote_store: dict[str, tuple[str, str]] = {}

    def runner(binary: str, args: Sequence[str], env: Mapping[str, str]) -> Result[str]:
        del binary, env
        if len(args) >= 2 and args[0] == "secret" and args[1] == "list":
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

    raw_doc = json.loads((store_dir / "payments.json").read_text(encoding="utf-8"))
    assert raw_doc["stripeApiKey"] == {
        "value": "payments/stripeApiKey",
        "provider": "bws",
    }

    assembled = client.get_namespace("payments", dict)
    assert assembled.ok
    assert assembled.value == {
        "webhookUrl": "https://pay.nyssa.ai/hook",
        "stripeApiKey": "sk_live_bws_secret",
    }

    removed = client.remove("payments", "stripeApiKey")
    assert removed.ok
    assert removed.value.remote_value_remains is False
    assert len(remote_store) == 0
