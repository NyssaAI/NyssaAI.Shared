# Responsibility: Define the public Configuration facade protocol for settings, secrets, OAuth, and recovery.
# Public interface:
# `-- Configuration
#     +-- authorize(namespace, key, options, browser_launcher)
#     +-- authorize_provider(provider_name, browser_launcher)
#     +-- get(namespace, key)
#     +-- get_as(namespace, key, target_type)
#     +-- get_namespace(namespace, target_type)
#     +-- list_keys(namespace)
#     +-- merge_settings(namespace, value)
#     +-- move(namespace, key, destination_provider)
#     +-- reauthorize(namespace, key, browser_launcher)
#     +-- recover(handle)
#     +-- remove(namespace, key)
#     +-- set(namespace, key, value, provider)
#     +-- set_as(namespace, key, value, provider)
#     `-- set_namespace(namespace, value)
"""Public Configuration protocol matching IConfiguration and IConfigurationExtensions."""

from collections.abc import Callable
from typing import Protocol

from nyssa_ai_configuration.oauth.browser_launcher import BrowserLauncher
from nyssa_ai_configuration.oauth.types import OAuthOptions
from nyssa_ai_configuration.recovery.configuration_recovery_handle import (
    ConfigurationRecoveryHandle,
)
from nyssa_ai_configuration.recovery.types import (
    ConfigurationAttempt,
    ConfigurationMove,
    ConfigurationRecoveryOutcome,
)
from nyssa_ai_configuration.results.result import Result
from nyssa_ai_configuration.values.types import ConfigurationRemoval


class Configuration(Protocol):
    """Unified interface for reading, writing, migrating, authorizing, and recovering configuration."""

    def get(self, namespace: str, key: str) -> Result[str]:
        """Read a string setting or secret at (namespace, key)."""
        ...

    def set(
        self,
        namespace: str,
        key: str,
        value: str,
        provider: str | None = None,
    ) -> Result[None]:
        """Write a string setting or secret at (namespace, key)."""
        ...

    def get_as[T](
        self,
        namespace: str,
        key: str,
        target_type: Callable[[object], T] | type[T],
    ) -> Result[T]:
        """Read and deserialize a typed DTO or primitive at (namespace, key)."""
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

    def get_namespace[T](
        self,
        namespace: str,
        target_type: Callable[[object], T] | type[T],
    ) -> Result[T]:
        """Assemble all entries in namespace and deserialize into target_type."""
        ...

    def set_namespace(self, namespace: str, value: object) -> ConfigurationAttempt[None]:
        """Decompose value into top-level namespace entries, replacing omitted local keys."""
        ...

    def merge_settings(self, namespace: str, value: object) -> ConfigurationAttempt[None]:
        """Decompose value and shallow-merge supplied top-level keys into namespace."""
        ...

    def list_keys(self, namespace: str) -> Result[list[str]]:
        """List all keys in namespace in UTF-16 code-unit ordinal order."""
        ...

    def remove(self, namespace: str, key: str) -> Result[ConfigurationRemoval]:
        """Remove (namespace, key) from its provider and local namespace file."""
        ...

    def move(
        self,
        namespace: str,
        key: str,
        destination_provider: str,
    ) -> ConfigurationAttempt[ConfigurationMove]:
        """Migrate (namespace, key) to destination_provider."""
        ...

    def authorize(
        self,
        namespace: str,
        key: str,
        options: OAuthOptions,
        browser_launcher: BrowserLauncher | None = None,
    ) -> Result[None]:
        """Run interactive OAuth 2.0 PKCE sign-in and store the grant at (namespace, key)."""
        ...

    def authorize_provider(
        self,
        provider_name: str,
        browser_launcher: BrowserLauncher | None = None,
    ) -> Result[None]:
        """Run interactive OAuth 2.0 PKCE sign-in for a configured cloud key vault's credential."""
        ...

    def reauthorize(
        self,
        namespace: str,
        key: str,
        browser_launcher: BrowserLauncher | None = None,
    ) -> Result[None]:
        """Re-run interactive OAuth 2.0 PKCE sign-in using endpoint metadata stored at (namespace, key)."""
        ...

    def recover(self, handle: ConfigurationRecoveryHandle) -> Result[ConfigurationRecoveryOutcome]:
        """Resume and reconcile an interrupted multi-step configuration operation."""
        ...
