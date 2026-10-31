# Responsibility: Verify ConfigurationRecoveryTokenProtector HKDF-SHA256 seal/open and ConfigurationRecoveryHandle redaction.
# Public interface:
# +-- test_recovery_handle_redacts_token_in_str_and_repr_and_validates_import()
# `-- test_recovery_token_protector_seals_and_rejects_wrong_application_or_root(tmp_path)
"""Unit tests for ConfigurationRecoveryHandle and ConfigurationRecoveryTokenProtector."""

from pathlib import Path
import uuid

from nyssa_ai_configuration.cloud_key_vaults.types import CloudKeyVaultOperation
from nyssa_ai_configuration.keyring.keyring import get_or_create_key
from nyssa_ai_configuration.keyring.keyring_in_memory import KeyringInMemory
from nyssa_ai_configuration.recovery.configuration_recovery_handle import (
    ConfigurationRecoveryHandle,
)
from nyssa_ai_configuration.recovery.configuration_recovery_token_protector import (
    ConfigurationRecoveryTokenProtector,
)
from nyssa_ai_configuration.recovery.types import (
    ConfigurationRecoveryState,
    ConfigurationRecoveryStep,
)
from nyssa_ai_configuration.values.types import ConfigurationValue


def test_recovery_handle_redacts_token_in_str_and_repr_and_validates_import() -> None:
    """Verify ConfigurationRecoveryHandle never leaks its token in str() or repr()."""
    imported = ConfigurationRecoveryHandle.import_token("secret-recovery-token-value")
    assert imported.ok
    handle = imported.value

    assert "secret-recovery-token-value" not in str(handle)
    assert "secret-recovery-token-value" not in repr(handle)
    assert handle.export() == "secret-recovery-token-value"

    blank = ConfigurationRecoveryHandle.import_token("   ")
    assert not blank.ok
    assert blank.category == "Validation"


def test_recovery_token_protector_seals_and_rejects_wrong_application_or_root(tmp_path: Path) -> None:
    """Verify HKDF-SHA256 + AES-256-GCM recovery token round-trips and binds to app and root."""
    keyring = KeyringInMemory()
    key_res = get_or_create_key("nyssa", keyring)
    assert key_res.ok

    root_a = str(tmp_path / "store_a")
    root_b = str(tmp_path / "store_b")

    protector_a = ConfigurationRecoveryTokenProtector(key_res.value, "nyssa", root_a)
    state = ConfigurationRecoveryState(
        operation_id=str(uuid.uuid4()),
        namespace="payments",
        kind="Move",
        original={"apiKey": ConfigurationValue(value="plain-secret")},
        proposed={"apiKey": ConfigurationValue(value="ptr-1", provider="bitwarden")},
        steps=(
            ConfigurationRecoveryStep(
                provider="bitwarden",
                operation=CloudKeyVaultOperation(
                    operation_id=str(uuid.uuid4()),
                    action="Write",
                    namespace="payments",
                    key="apiKey",
                    value="plain-secret",
                ),
                local=False,
                is_json_value=False,
                state="Unknown",
            ),
        ),
        publication="NotAttempted",
    )

    sealed = protector_a.seal(state)
    assert sealed.ok

    opened = protector_a.open(sealed.value)
    assert opened.ok
    assert opened.value.operation_id == state.operation_id
    assert opened.value.namespace == "payments"
    assert len(opened.value.steps) == 1

    protector_wrong_root = ConfigurationRecoveryTokenProtector(key_res.value, "nyssa", root_b)
    wrong_root_res = protector_wrong_root.open(sealed.value)
    assert not wrong_root_res.ok
    assert wrong_root_res.category == "Validation"

    protector_wrong_app = ConfigurationRecoveryTokenProtector(key_res.value, "other-app", root_a)
    wrong_app_res = protector_wrong_app.open(sealed.value)
    assert not wrong_app_res.ok
