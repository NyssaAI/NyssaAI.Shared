# Responsibility: Define the ConfigurationMaintainer protocol for provider migration and interrupted-operation recovery.
# Public interface:
# `-- ConfigurationMaintainer
#     +-- move(namespace, key, destination_provider)
#     `-- recover(handle)
"""Maintenance protocol for migrating configuration providers and recovering interrupted operations."""

from typing import Protocol

from nyssa_ai_configuration.recovery.configuration_recovery_handle import (
    ConfigurationRecoveryHandle,
)
from nyssa_ai_configuration.recovery.types import (
    ConfigurationAttempt,
    ConfigurationMove,
    ConfigurationRecoveryOutcome,
)
from nyssa_ai_configuration.results.result import Result


class ConfigurationMaintainer(Protocol):
    """Interface for migrating configuration entries across providers and recovering interrupted operations."""

    def move(
        self,
        namespace: str,
        key: str,
        destination_provider: str,
    ) -> ConfigurationAttempt[ConfigurationMove]:
        """Migrate (namespace, key) to destination_provider."""
        ...

    def recover(self, handle: ConfigurationRecoveryHandle) -> Result[ConfigurationRecoveryOutcome]:
        """Resume and reconcile an interrupted multi-step configuration operation."""
        ...
