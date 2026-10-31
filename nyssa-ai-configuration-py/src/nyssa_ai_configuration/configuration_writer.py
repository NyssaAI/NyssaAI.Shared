# Responsibility: Define the ConfigurationWriter protocol for writing, merging, and removing configuration values.
# Public interface:
# `-- ConfigurationWriter
#     +-- merge_settings(namespace, value)
#     +-- remove(namespace, key)
#     +-- set(namespace, key, value, provider)
#     +-- set_as(namespace, key, value, provider)
#     `-- set_namespace(namespace, value)
"""Write protocol for storing, merging, and removing settings and secrets."""

from typing import Protocol

from nyssa_ai_configuration.recovery.types import ConfigurationAttempt
from nyssa_ai_configuration.results.result import Result
from nyssa_ai_configuration.values.types import ConfigurationRemoval


class ConfigurationWriter(Protocol):
    """Interface for writing, replacing, merging, and removing configuration entries."""

    def set(
        self,
        namespace: str,
        key: str,
        value: str,
        provider: str | None = None,
    ) -> Result[None]:
        """Write a string setting or secret at (namespace, key)."""
        ...

    def set_as(
        self,
        namespace: str,
        key: str,
        value: object,
        provider: str | None = None,
    ) -> Result[None]:
        """Serialize and write a typed DTO or primitive at (namespace, key)."""
        ...

    def set_namespace(self, namespace: str, value: object) -> ConfigurationAttempt[None]:
        """Decompose value into top-level namespace entries, replacing omitted local keys."""
        ...

    def merge_settings(self, namespace: str, value: object) -> ConfigurationAttempt[None]:
        """Decompose value and shallow-merge supplied top-level keys into namespace."""
        ...

    def remove(self, namespace: str, key: str) -> Result[ConfigurationRemoval]:
        """Remove (namespace, key) from its provider and local namespace file."""
        ...
