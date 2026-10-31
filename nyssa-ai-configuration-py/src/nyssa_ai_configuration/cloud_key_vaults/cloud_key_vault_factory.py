# Responsibility: Define the CloudKeyVaultFactory protocol for creating cloud key vault adapters from VaultOptions.
# Public interface:
# `-- CloudKeyVaultFactory
#     +-- authorization(options)
#     +-- create(options, token_source)
#     `-- kind
"""Factory protocol for instantiating CloudKeyVault adapters."""

from typing import Protocol

from nyssa_ai_configuration.cloud_key_vaults.access_token_source import AccessTokenSource
from nyssa_ai_configuration.cloud_key_vaults.cloud_key_vault import CloudKeyVault
from nyssa_ai_configuration.manifest.types import VaultOptions
from nyssa_ai_configuration.oauth.types import OAuthOptions
from nyssa_ai_configuration.results.result import Result


class CloudKeyVaultFactory(Protocol):
    """Creates a CloudKeyVault instance for a matching VaultOptions.kind."""

    @property
    def kind(self) -> str:
        """Return the vault kind identifier (e.g. 'bitwarden-secrets', 'azure-key-vault')."""
        ...

    def create(
        self,
        options: VaultOptions,
        token_source: AccessTokenSource | None = None,
    ) -> Result[CloudKeyVault]:
        """Instantiate a CloudKeyVault from options and optional OAuth token_source."""
        ...

    def authorization(self, options: VaultOptions) -> Result[OAuthOptions | None]:
        """Return OAuthOptions when this vault kind supports interactive OAuth sign-in, or Ok(None)."""
        ...
