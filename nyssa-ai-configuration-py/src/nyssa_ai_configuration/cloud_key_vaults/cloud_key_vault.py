# Responsibility: Define the CloudKeyVault protocol for reading, writing, and removing remote vault secrets.
# Public interface:
# `-- CloudKeyVault
#     +-- name
#     +-- read(pointer)
#     +-- recovery
#     +-- remove(pointer)
#     `-- write(namespace, key, value)
"""Protocol implemented by external cloud key vault adapters."""

from typing import Protocol

from nyssa_ai_configuration.cloud_key_vaults.cloud_key_vault_recovery import (
    CloudKeyVaultRecovery,
)
from nyssa_ai_configuration.cloud_key_vaults.types import ConfigurationPointer
from nyssa_ai_configuration.results.result import Result


class CloudKeyVault(Protocol):
    """Contract for reading, writing, and removing secrets in a remote cloud key vault."""

    @property
    def name(self) -> str:
        """Return the configured registration name of this vault instance."""
        ...

    @property
    def recovery(self) -> CloudKeyVaultRecovery | None:
        """Return the optional CloudKeyVaultRecovery capability if supported by this vault."""
        ...

    def read(self, pointer: ConfigurationPointer) -> Result[str]:
        """Read the cleartext secret referenced by pointer."""
        ...

    def write(self, namespace: str, key: str, value: str) -> Result[ConfigurationPointer]:
        """Write value for (namespace, key) to the remote vault and return its ConfigurationPointer."""
        ...

    def remove(self, pointer: ConfigurationPointer) -> Result[bool]:
        """Remove pointer from the remote vault; return Ok(True) if remote value remains (soft-delete/retained) or Ok(False) if purged."""
        ...
