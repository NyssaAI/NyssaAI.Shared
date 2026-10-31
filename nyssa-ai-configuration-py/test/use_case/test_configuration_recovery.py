# Responsibility: Exercise F-204 two-phase recovery coordination, progress receipts, opaque recovery handles, and conflict detection.
# Public interface:
# +-- GuardedVault
# |   +-- execute(operation, preparation)
# |   +-- name
# |   +-- new(store_id)
# |   +-- prepare(operation)
# |   +-- read(pointer)
# |   +-- reconcile(operation, preparation)
# |   +-- recovery
# |   +-- remove(pointer)
# |   +-- store_identity
# |   `-- write(namespace, key, secret)
# +-- GuardedVaultFactory
# |   +-- authorization(options)
# |   +-- create(options, token_source)
# |   +-- kind
# |   `-- new(vault)
# +-- test_interrupted_move_returns_recovery_handle_and_recovers_to_completed(tmp_path)
# +-- test_interrupted_set_namespace_recovers_and_detects_concurrent_conflict(tmp_path)
# `-- test_unguarded_vault_reports_manual_reconciliation_when_interrupted(tmp_path)
"""Use-case tests for F-204 multi-step recovery coordination, handle export/import, and conflict detection."""

from pathlib import Path

from nyssa_ai_configuration import (
    AccessTokenSource,
    BootstrapOptions,
    CloudKeyVault,
    CloudKeyVaultOperation,
    CloudKeyVaultPreparation,
    CloudKeyVaultRecovery,
    CloudKeyVaultStepEffect,
    ConfigurationClient,
    ConfigurationPointer,
    ConfigurationRecoveryHandle,
    KeyringInMemory,
    OAuthOptions,
    Result,
    VaultOptions,
    fail,
    ok,
)


class GuardedVault:
    """CloudKeyVault implementing CloudKeyVaultRecovery for deterministic F-204 recovery testing."""

    def __init__(self, store_id: str = "vault-instance-01") -> None:
        """Initialize with store identity and in-memory secret map."""
        self._store_id = store_id
        self.items: dict[str, str] = {}
        self._executed_ops: dict[str, ConfigurationPointer | None] = {}

    @property
    def name(self) -> str:
        """Return provider instance name."""
        return "bitwarden"

    @property
    def store_identity(self) -> str:
        """Return stable vault store identity."""
        return self._store_id

    @property
    def recovery(self) -> CloudKeyVaultRecovery | None:
        """Return self as CloudKeyVaultRecovery."""
        return self

    def prepare(self, operation: CloudKeyVaultOperation) -> Result[CloudKeyVaultPreparation]:
        """Prepare a deterministic target reference for write or remove."""
        ref = (
            operation.pointer.value
            if operation.pointer is not None
            else f"guarded://{operation.namespace}/{operation.key}"
        )
        return ok(CloudKeyVaultPreparation(store_identity=self._store_id, target_reference=ref))

    def execute(
        self,
        operation: CloudKeyVaultOperation,
        preparation: CloudKeyVaultPreparation,
    ) -> Result[CloudKeyVaultStepEffect]:
        """Execute prepared write or remove and record idempotency state."""
        ref = preparation.target_reference or f"guarded://{operation.namespace}/{operation.key}"
        if operation.action == "Write":
            assert operation.value is not None
            self.items[ref] = operation.value
            ptr = ConfigurationPointer(provider="bitwarden", value=ref)
            self._executed_ops[operation.operation_id] = ptr
            return ok(CloudKeyVaultStepEffect(state="Applied", pointer=ptr))
        self.items.pop(ref, None)
        self._executed_ops[operation.operation_id] = None
        return ok(CloudKeyVaultStepEffect(state="Applied"))

    def reconcile(
        self,
        operation: CloudKeyVaultOperation,
        preparation: CloudKeyVaultPreparation,
    ) -> Result[CloudKeyVaultStepEffect]:
        """Reconcile whether operation was already applied before an interruption."""
        del preparation
        if operation.operation_id in self._executed_ops:
            return ok(CloudKeyVaultStepEffect(state="Applied", pointer=self._executed_ops[operation.operation_id]))
        return ok(CloudKeyVaultStepEffect(state="Failed"))

    def read(self, pointer: ConfigurationPointer) -> Result[str]:
        """Read secret by pointer."""
        return ok(self.items[pointer.value])

    def write(self, namespace: str, key: str, secret: str) -> Result[ConfigurationPointer]:
        """Write secret directly when unguarded."""
        ref = f"guarded://{namespace}/{key}"
        self.items[ref] = secret
        return ok(ConfigurationPointer(provider="bitwarden", value=ref))

    def remove(self, pointer: ConfigurationPointer) -> Result[bool]:
        """Remove secret directly when unguarded."""
        self.items.pop(pointer.value, None)
        return ok(False)


class GuardedVaultFactory:
    """Factory returning a GuardedVault for kind 'bitwarden'."""

    def __init__(self, vault: CloudKeyVault) -> None:
        """Initialize with vault."""
        self._vault = vault

    @property
    def kind(self) -> str:
        """Return provider kind."""
        return "bitwarden"

    def create(self, options: VaultOptions, token_source: AccessTokenSource | None = None) -> Result[CloudKeyVault]:
        """Return the GuardedVault."""
        del options, token_source
        return ok(self._vault)

    def authorization(self, options: VaultOptions) -> Result[OAuthOptions | None]:
        """Return None for OAuth options."""
        del options
        return ok(None)


def test_interrupted_move_returns_recovery_handle_and_recovers_to_completed(tmp_path: Path) -> None:
    """Verify an interrupted move to a guarded cloud vault produces a recovery handle that completes via recover()."""
    vault = GuardedVault()
    keyring = KeyringInMemory()
    created = ConfigurationClient.create(
        BootstrapOptions(
            application_name="nyssa-app",
            configuration_directory=str(tmp_path / "store"),
            manifest_path=str(tmp_path / "config-manifest.json"),
            key_vault=VaultOptions(name="bitwarden", kind="bitwarden"),
        ),
        keyring=keyring,
        vault_factories=[GuardedVaultFactory(vault)],
    )
    assert created.ok
    client = created.value

    assert client.set("payments", "secretKey", "sk_live_abc123", provider="aesLocal").ok

    # Simulate a local disk write failure after the remote vault write succeeds
    def fail_local_publish(namespace: str) -> Result[None]:
        return fail(f"Simulated crash publishing {namespace}.json", "Unavailable")

    client.set_document_write_interceptor(fail_local_publish)
    attempt = client.move("payments", "secretKey", "bitwarden")

    assert not attempt.result.ok
    assert attempt.progress is not None
    assert attempt.progress.recovery == "Unfinished"
    assert attempt.recovery_handle is not None

    # Export and re-import the opaque recovery token across process boundaries
    exported_token = attempt.recovery_handle.export()
    reimported = ConfigurationRecoveryHandle.import_token(exported_token)
    assert reimported.ok

    # Restore local disk writes and recover
    client.set_document_write_interceptor(None)
    recovered = client.recover(reimported.value)
    assert recovered.ok
    assert recovered.value.status == "Completed"
    assert recovered.value.recovery_handle is None

    val_res = client.get("payments", "secretKey")
    assert val_res.ok
    assert val_res.value == "sk_live_abc123"


def test_interrupted_set_namespace_recovers_and_detects_concurrent_conflict(tmp_path: Path) -> None:
    """Verify recover() detects Conflict if a namespace setting was modified externally after an interrupted set_namespace."""
    vault = GuardedVault()
    created = ConfigurationClient.create(
        BootstrapOptions(
            application_name="nyssa-app",
            configuration_directory=str(tmp_path / "store"),
            manifest_path=str(tmp_path / "config-manifest.json"),
            key_vault=VaultOptions(name="bitwarden", kind="bitwarden"),
        ),
        keyring=KeyringInMemory(),
        vault_factories=[GuardedVaultFactory(vault)],
    )
    assert created.ok
    client = created.value

    assert client.set("payments", "vaultKey", "v1", provider="bitwarden").ok
    assert client.set("payments", "region", "us-west").ok

    def fail_publish(namespace: str) -> Result[None]:
        return fail(f"Crash writing {namespace}", "Unavailable")

    client.set_document_write_interceptor(fail_publish)
    interrupted = client.set_namespace("payments", {"vaultKey": "v2", "region": "eu-central"})
    assert not interrupted.result.ok
    assert interrupted.recovery_handle is not None

    # External modification occurs before recover() is called
    client.set_document_write_interceptor(None)
    assert client.set("payments", "region", "ap-south").ok

    conflict_outcome = client.recover(interrupted.recovery_handle)
    assert conflict_outcome.ok
    assert conflict_outcome.value.status == "Conflict"


def test_unguarded_vault_reports_manual_reconciliation_when_interrupted(tmp_path: Path) -> None:
    """Verify an interrupted multi-step write across a vault without CloudKeyVaultRecovery reports ManualReconciliation."""

    class UnguardedVault:
        def __init__(self) -> None:
            self.items: dict[str, str] = {}
            self.recovery: CloudKeyVaultRecovery | None = None

        @property
        def name(self) -> str:
            return "bitwarden"

        def read(self, pointer: ConfigurationPointer) -> Result[str]:
            return ok(self.items[pointer.value])

        def write(self, namespace: str, key: str, secret: str) -> Result[ConfigurationPointer]:
            ref = f"unguarded://{namespace}/{key}"
            self.items[ref] = secret
            return ok(ConfigurationPointer(provider="bitwarden", value=ref))

        def remove(self, pointer: ConfigurationPointer) -> Result[bool]:
            self.items.pop(pointer.value, None)
            return ok(False)

    vault = UnguardedVault()
    created = ConfigurationClient.create(
        BootstrapOptions(
            application_name="nyssa-app",
            configuration_directory=str(tmp_path / "store"),
            manifest_path=str(tmp_path / "config-manifest.json"),
            key_vault=VaultOptions(name="bitwarden", kind="bitwarden"),
        ),
        keyring=KeyringInMemory(),
        vault_factories=[GuardedVaultFactory(vault)],
    )
    assert created.ok
    client = created.value

    assert client.set("payments", "vaultKey", "v1", provider="bitwarden").ok

    def fail_publish(namespace: str) -> Result[None]:
        return fail(f"Crash writing {namespace}", "Unavailable")

    client.set_document_write_interceptor(fail_publish)
    attempt = client.merge_settings("payments", {"vaultKey": "v2"})

    assert not attempt.result.ok
    assert attempt.progress is not None
    assert attempt.progress.recovery == "ManualReconciliation"
    assert attempt.recovery_handle is None
