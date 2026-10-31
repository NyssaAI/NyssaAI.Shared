# Responsibility: Define the read-only ConfigurationReader protocol for querying settings, secrets, and OAuth tokens.
# Public interface:
# `-- ConfigurationReader
#     +-- get(namespace, key)
#     +-- get_as(namespace, key, target_type)
#     +-- get_namespace(namespace, target_type)
#     `-- list(namespace)
"""Read-only configuration protocol for querying string, typed, and namespace values."""

from __future__ import annotations

from collections.abc import Callable
from typing import Protocol

from nyssa_ai_configuration.results.result import Result


class ConfigurationReader(Protocol):
    """Read-only interface for resolving settings, secrets, and OAuth access tokens."""

    def get(self, namespace: str, key: str) -> Result[str]:
        """Read a string setting, secret, or OAuth access token at (namespace, key)."""
        ...

    def get_as[T](
        self,
        namespace: str,
        key: str,
        target_type: Callable[[object], T] | type[T],
    ) -> Result[T]:
        """Read and deserialize a typed DTO or primitive at (namespace, key)."""
        ...

    def get_namespace[T](
        self,
        namespace: str,
        target_type: Callable[[object], T] | type[T],
    ) -> Result[T]:
        """Assemble all entries in namespace and deserialize into target_type."""
        ...

    def list(self, namespace: str) -> Result[list[str]]:
        """List all keys in namespace in UTF-16 code-unit ordinal order."""
        ...
