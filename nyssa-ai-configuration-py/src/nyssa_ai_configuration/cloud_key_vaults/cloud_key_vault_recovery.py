# Responsibility: Define the CloudKeyVaultRecovery protocol for guarded two-phase prepare, execute, and reconcile operations.
# Public interface:
# `-- CloudKeyVaultRecovery
#     +-- execute(operation, preparation)
#     +-- prepare(operation)
#     +-- reconcile(operation, preparation)
#     `-- store_identity
"""Optional recovery protocol implemented by cloud key vaults supporting F-204 two-phase recovery."""

from typing import Protocol

from nyssa_ai_configuration.cloud_key_vaults.types import (
    CloudKeyVaultOperation,
    CloudKeyVaultPreparation,
    CloudKeyVaultStepEffect,
)
from nyssa_ai_configuration.results.result import Result


class CloudKeyVaultRecovery(Protocol):
    """Guarded two-phase recovery capability for a cloud key vault."""

    @property
    def store_identity(self) -> str:
        """Return a stable backend store/tenant/account identity independent of registration name."""
        ...

    def prepare(self, operation: CloudKeyVaultOperation) -> Result[CloudKeyVaultPreparation]:
        """Capture target state and concurrency preconditions before mutating the vault."""
        ...

    def execute(
        self,
        operation: CloudKeyVaultOperation,
        preparation: CloudKeyVaultPreparation,
    ) -> Result[CloudKeyVaultStepEffect]:
        """Execute the conditional mutation against the prepared snapshot."""
        ...

    def reconcile(
        self,
        operation: CloudKeyVaultOperation,
        preparation: CloudKeyVaultPreparation,
    ) -> Result[CloudKeyVaultStepEffect]:
        """Determine whether an interrupted mutation was Applied, Failed, Conflict, Retained, or Manual."""
        ...
