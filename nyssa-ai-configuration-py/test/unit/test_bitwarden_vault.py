# Responsibility: Unit tests for CloudKeyVaultBitwarden and CloudKeyVaultFactoryBitwarden CLI argument construction, 404 empty-project handling, and token resolution.
# Public interface:
# +-- test_bitwarden_factory_validates_credential_and_creates_vault()
# +-- test_bitwarden_vault_create_edit_read_and_remove_workflow()
# `-- test_bitwarden_vault_requires_project_id_for_creating_new_secret()
"""Unit tests for CloudKeyVaultBitwarden and CloudKeyVaultFactoryBitwarden."""

from __future__ import annotations

import json
from collections.abc import Mapping, Sequence

from nyssa_ai_configuration import (
    CloudKeyVaultBitwarden,
    CloudKeyVaultFactoryBitwarden,
    ConfigurationPointer,
    Result,
    VaultOptions,
    fail,
    ok,
)


def test_bitwarden_factory_validates_credential_and_creates_vault() -> None:
    """Verify CloudKeyVaultFactoryBitwarden rejects missing credentials and succeeds with accessToken."""
    factory = CloudKeyVaultFactoryBitwarden()
    assert factory.kind == "bitwarden-secrets"

    missing_cred = factory.create(VaultOptions(name="bws", kind="bitwarden-secrets"))
    assert not missing_cred.ok
    assert missing_cred.category == "Validation"

    valid = factory.create(
        VaultOptions(
            name="bws",
            kind="bitwarden-secrets",
            settings={"projectId": "proj-1"},
            secrets={"accessToken": "tok-1"},
        )
    )
    assert valid.ok
    assert valid.value.name == "bws"
    assert valid.value.recovery is None


def test_bitwarden_vault_create_edit_read_and_remove_workflow() -> None:
    """Verify CloudKeyVaultBitwarden handles 404 empty-project list, creates, edits, reads, and deletes secrets."""
    store: dict[str, tuple[str, str]] = {}
    calls: list[tuple[str, list[str], dict[str, str]]] = []
    empty_404 = True

    def runner(binary: str, args: Sequence[str], env: Mapping[str, str]) -> Result[str]:
        calls.append((binary, list(args), dict(env)))
        if len(args) >= 2 and args[0] == "secret" and args[1] == "list":
            if empty_404 and not store:
                return fail("Error: [404 Not Found] Resource not found", "Unavailable")
            payload = [
                {"id": item_id, "key": item_key, "value": item_val}
                for item_id, (item_key, item_val) in store.items()
            ]
            return ok(json.dumps(payload))
        if len(args) >= 5 and args[0] == "secret" and args[1] == "create":
            secret_key, secret_val = args[2], args[3]
            new_id = f"id-{len(store) + 1}"
            store[new_id] = (secret_key, secret_val)
            return ok(json.dumps({"id": new_id, "key": secret_key, "value": secret_val}))
        if len(args) >= 5 and args[0] == "secret" and args[1] == "edit":
            target_id, new_val = args[2], args[4]
            old_key, _ = store[target_id]
            store[target_id] = (old_key, new_val)
            return ok(json.dumps({"id": target_id, "key": old_key, "value": new_val}))
        if len(args) >= 3 and args[0] == "secret" and args[1] == "get":
            target_id = args[2]
            item_key, item_val = store[target_id]
            return ok(json.dumps({"id": target_id, "key": item_key, "value": item_val}))
        if len(args) >= 3 and args[0] == "secret" and args[1] == "delete":
            target_id = args[2]
            store.pop(target_id, None)
            return ok("")
        return fail("Unexpected bws command", "Unavailable")

    options = VaultOptions(
        name="bws-prod",
        kind="bitwarden-secrets",
        endpoint="https://vault.example.com",
        settings={"projectId": "proj-123", "organizationId": "org-456", "cliPath": "/usr/local/bin/bws"},
        secrets={"accessToken": "machine-token-abc"},
    )
    vault = CloudKeyVaultBitwarden(options, command_runner=runner)

    # 1. Write new secret when list returns 404 empty project
    created_ptr = vault.write("api", "token", "secret-v1")
    assert created_ptr.ok
    assert created_ptr.value == ConfigurationPointer(provider="bws-prod", value="api/token")
    assert calls[0][0] == "/usr/local/bin/bws"
    assert calls[0][2][CloudKeyVaultBitwarden.ACCESS_TOKEN_ENV] == "machine-token-abc"
    assert calls[0][2][CloudKeyVaultBitwarden.ORGANIZATION_ID_ENV] == "org-456"
    assert calls[0][2][CloudKeyVaultBitwarden.SERVER_URL_ENV] == "https://vault.example.com"

    # 2. Read secret
    read_v1 = vault.read(created_ptr.value)
    assert read_v1.ok
    assert read_v1.value == "secret-v1"

    # 3. Edit existing secret
    edited_ptr = vault.write("api", "token", "secret-v2")
    assert edited_ptr.ok
    read_v2 = vault.read(edited_ptr.value)
    assert read_v2.ok
    assert read_v2.value == "secret-v2"

    # 4. Remove secret
    removed = vault.remove(edited_ptr.value)
    assert removed.ok
    assert removed.value is False

    # 5. Read after remove returns NotFound
    missing = vault.read(edited_ptr.value)
    assert not missing.ok
    assert missing.category == "NotFound"


def test_bitwarden_vault_requires_project_id_for_creating_new_secret() -> None:
    """Verify CloudKeyVaultBitwarden returns a Validation failure when creating a new secret without projectId."""

    def runner(binary: str, args: Sequence[str], env: Mapping[str, str]) -> Result[str]:
        del binary, args, env
        return ok("[]")

    vault = CloudKeyVaultBitwarden(
        VaultOptions(
            name="bws",
            kind="bitwarden-secrets",
            secrets={"accessToken": "tok"},
        ),
        command_runner=runner,
    )
    res = vault.write("ns", "key", "val")
    assert not res.ok
    assert res.category == "Validation"
