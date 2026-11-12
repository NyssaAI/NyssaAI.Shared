# Responsibility: Unit tests for CloudKeyVaultBitwarden single-call read, fallback get, and CloudKeyVaultCaching TTL/eviction/fail-open semantics.
# Public interface:
# +-- test_bitwarden_factory_validates_credential_and_creates_vault()
# +-- test_bitwarden_vault_create_edit_read_and_remove_workflow()
# +-- test_bitwarden_vault_falls_back_to_secret_get_when_list_omits_value()
# +-- test_bitwarden_vault_requires_project_id_for_creating_new_secret()
# `-- test_cloud_key_vault_caching_ttl_eviction_scope_and_fail_open()
"""Unit tests for CloudKeyVaultBitwarden, CloudKeyVaultFactoryBitwarden, and CloudKeyVaultCaching."""

from __future__ import annotations

from collections.abc import Mapping, Sequence
import json

from nyssa_ai_configuration import (
    CloudKeyVaultAction,
    CloudKeyVaultBitwarden,
    CloudKeyVaultCaching,
    CloudKeyVaultFactoryBitwarden,
    CloudKeyVaultOperation,
    CloudKeyVaultPreparation,
    CloudKeyVaultRecovery,
    CloudKeyVaultStepEffect,
    ConfigurationPointer,
    ConfigurationStepState,
    Result,
    SecretCacheInMemory,
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
    """Verify CloudKeyVaultBitwarden resolves read in a single secret list call when value is present."""
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

    # 2. Read secret -> resolves directly from secret list without invoking secret get
    calls_before_read = len(calls)
    read_v1 = vault.read(created_ptr.value)
    assert read_v1.ok
    assert read_v1.value == "secret-v1"
    assert len(calls) == calls_before_read + 1
    assert calls[-1][1][:2] == ["secret", "list"]

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


def test_bitwarden_vault_falls_back_to_secret_get_when_list_omits_value() -> None:
    """Verify CloudKeyVaultBitwarden falls back to 'bws secret get <id>' when 'bws secret list' omits value."""
    calls: list[list[str]] = []

    def runner(binary: str, args: Sequence[str], env: Mapping[str, str]) -> Result[str]:
        del binary, env
        calls.append(list(args))
        if args[:2] == ["secret", "list"]:
            return ok(json.dumps([{"id": "uuid-99", "key": "api/token"}]))
        if args[:3] == ["secret", "get", "uuid-99"]:
            return ok(json.dumps({"id": "uuid-99", "key": "api/token", "value": "fallback-secret"}))
        return fail("Unexpected command", "Unavailable")

    vault = CloudKeyVaultBitwarden(
        VaultOptions(
            name="bws",
            kind="bitwarden-secrets",
            settings={"projectId": "proj-1"},
            secrets={"accessToken": "tok"},
        ),
        command_runner=runner,
    )
    res = vault.read(ConfigurationPointer(provider="bws", value="api/token"))
    assert res.ok
    assert res.value == "fallback-secret"
    assert len(calls) == 2
    assert calls[0][:2] == ["secret", "list"]
    assert calls[1][:3] == ["secret", "get", "uuid-99"]


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


def test_cloud_key_vault_caching_ttl_eviction_scope_and_fail_open() -> None:
    """Verify CloudKeyVaultCaching default 60s TTL, expiry, write/remove/recovery eviction, scope isolation, and fail-open."""
    now = [100.0]
    read_calls = [0]
    backing: dict[str, str] = {"payments/apiKey": "v1"}

    class _FakeInnerVault:
        @property
        def name(self) -> str:
            return "bitwarden"

        @property
        def store_identity(self) -> str:
            return "store-1"

        @property
        def recovery(self) -> CloudKeyVaultRecovery | None:
            return self

        def read(self, pointer: ConfigurationPointer) -> Result[str]:
            read_calls[0] += 1
            if pointer.value not in backing:
                return fail("missing", "NotFound")
            return ok(backing[pointer.value])

        def write(self, namespace: str, key: str, value: str) -> Result[ConfigurationPointer]:
            ptr = f"{namespace}/{key}"
            backing[ptr] = value
            return ok(ConfigurationPointer(provider="bitwarden", value=ptr))

        def remove(self, pointer: ConfigurationPointer) -> Result[bool]:
            backing.pop(pointer.value, None)
            return ok(False)

        def prepare(self, operation: CloudKeyVaultOperation) -> Result[CloudKeyVaultPreparation]:
            return ok(
                CloudKeyVaultPreparation(
                    store_identity="store-1",
                    target_reference=f"{operation.namespace}/{operation.key}",
                )
            )

        def execute(
            self,
            operation: CloudKeyVaultOperation,
            preparation: CloudKeyVaultPreparation,
        ) -> Result[CloudKeyVaultStepEffect]:
            del preparation
            ptr = f"{operation.namespace}/{operation.key}"
            if operation.action == "Write" and operation.value is not None:
                backing[ptr] = operation.value
            return ok(
                CloudKeyVaultStepEffect(
                    state="Applied",
                    pointer=ConfigurationPointer(provider="bitwarden", value=ptr),
                )
            )

        def reconcile(
            self,
            operation: CloudKeyVaultOperation,
            preparation: CloudKeyVaultPreparation,
        ) -> Result[CloudKeyVaultStepEffect]:
            return self.execute(operation, preparation)

    shared_cache = SecretCacheInMemory(clock=lambda: now[0])
    dev_opts = VaultOptions(name="bitwarden", kind="bitwarden-secrets", settings={"projectId": "proj-dev"})
    prod_opts = VaultOptions(name="bitwarden", kind="bitwarden-secrets", settings={"projectId": "proj-prod"})

    wrapped_dev = CloudKeyVaultCaching.wrap(_FakeInnerVault(), dev_opts, cache=shared_cache, clock=lambda: now[0])
    assert wrapped_dev.ok
    dev_vault = wrapped_dev.value
    assert isinstance(dev_vault, CloudKeyVaultCaching)
    assert dev_vault.cache_ttl_seconds == 60

    ptr = ConfigurationPointer(provider="bitwarden", value="payments/apiKey")
    r1 = dev_vault.read(ptr)
    assert r1.ok and r1.value == "v1"
    assert read_calls[0] == 1

    # Cache hit before 60s TTL
    now[0] += 59.0
    r2 = dev_vault.read(ptr)
    assert r2.ok and r2.value == "v1"
    assert read_calls[0] == 1

    # Scope isolation: prod vault with same shared_cache and pointer does not read dev's cached secret
    backing["payments/apiKey"] = "prod-v1"
    wrapped_prod = CloudKeyVaultCaching.wrap(_FakeInnerVault(), prod_opts, cache=shared_cache, clock=lambda: now[0])
    assert wrapped_prod.ok
    rp = wrapped_prod.value.read(ptr)
    assert rp.ok and rp.value == "prod-v1"
    assert read_calls[0] == 2

    # Expiry at >= 60s
    backing["payments/apiKey"] = "v2"
    now[0] += 2.0
    r3 = dev_vault.read(ptr)
    assert r3.ok and r3.value == "v2"
    assert read_calls[0] == 3

    # Write evicts immediately
    assert dev_vault.write("payments", "apiKey", "v3").ok
    r4 = dev_vault.read(ptr)
    assert r4.ok and r4.value == "v3"
    assert read_calls[0] == 4

    # Two-phase recovery execute evicts immediately
    op = CloudKeyVaultOperation(
        operation_id="op-1",
        action="Write",
        namespace="payments",
        key="apiKey",
        value="v4",
    )
    assert dev_vault.recovery is not None
    prep = dev_vault.recovery.prepare(op)
    assert prep.ok
    assert dev_vault.recovery.execute(op, prep.value).ok
    r5 = dev_vault.read(ptr)
    assert r5.ok and r5.value == "v4"
    assert read_calls[0] == 5

    # Remove evicts immediately
    assert dev_vault.remove(ptr).ok
    assert not dev_vault.read(ptr).ok
    assert read_calls[0] == 6

    # Validation of invalid cacheTTLSeconds
    neg_res = CloudKeyVaultCaching.resolve_ttl_seconds(
        VaultOptions(name="bitwarden", kind="bitwarden-secrets", cache_ttl_seconds=-1)
    )
    assert not neg_res.ok
    assert neg_res.category == "Validation"

    # Fail-open when custom cache raises
    class _BrokenCache:
        def get(self, key: str) -> str | None:
            raise RuntimeError(f"keyring error on {key}")

        def set(self, key: str, value: str, ttl_seconds: int) -> None:
            raise RuntimeError(f"keyring quota exceeded {key}={value} ({ttl_seconds})")

        def remove(self, key: str) -> None:
            raise RuntimeError(f"keyring remove failed {key}")

        def clear(self) -> None:
            raise RuntimeError("keyring clear failed")

    backing["payments/apiKey"] = "resilient-val"
    resilient = CloudKeyVaultCaching(_FakeInnerVault(), cache_ttl_seconds=60, cache=_BrokenCache())
    rr = resilient.read(ptr)
    assert rr.ok and rr.value == "resilient-val"
    assert resilient.write("payments", "apiKey", "resilient-val-2").ok
    resilient.clear_cache()
