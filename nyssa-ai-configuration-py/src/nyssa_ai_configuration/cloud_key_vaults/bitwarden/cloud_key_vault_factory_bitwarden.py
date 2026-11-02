# Responsibility: Factory creating CloudKeyVaultBitwarden adapters for Bitwarden Secrets Manager (bws).
# Public interface:
# `-- CloudKeyVaultFactoryBitwarden
#     +-- DEFAULT_KIND
#     +-- LEGACY_KIND
#     +-- SHORT_KIND
#     +-- authorization(options)
#     +-- create(options, token_source)
#     +-- kind
#     `-- new(kind, command_runner)
"""Factory for instantiating CloudKeyVaultBitwarden instances."""

from __future__ import annotations

from collections.abc import Callable, Mapping, Sequence

from nyssa_ai_result import Result, fail, ok

from nyssa_ai_configuration.cloud_key_vaults.access_token_source import AccessTokenSource
from nyssa_ai_configuration.cloud_key_vaults.bitwarden.cloud_key_vault_bitwarden import (
    CloudKeyVaultBitwarden,
)
from nyssa_ai_configuration.cloud_key_vaults.cloud_key_vault import CloudKeyVault
from nyssa_ai_configuration.manifest.types import VaultOptions
from nyssa_ai_configuration.oauth.types import OAuthOptions

type _BwsCommandRunner = Callable[[str, Sequence[str], Mapping[str, str]], Result[str]]


class CloudKeyVaultFactoryBitwarden:
    """Creates CloudKeyVaultBitwarden instances for a matching VaultOptions.kind."""

    DEFAULT_KIND: str = "bitwarden-secrets"
    SHORT_KIND: str = "bws"
    LEGACY_KIND: str = "bitwarden"

    def __init__(
        self,
        kind: str = DEFAULT_KIND,
        command_runner: _BwsCommandRunner | None = None,
    ) -> None:
        """Initialize factory with target vault kind and optional CLI command runner."""
        self._kind = kind.strip() if kind and kind.strip() else self.DEFAULT_KIND
        self._runner = command_runner

    @property
    def kind(self) -> str:
        """Return the vault kind identifier handled by this factory."""
        return self._kind

    def create(
        self,
        options: VaultOptions,
        token_source: AccessTokenSource | None = None,
    ) -> Result[CloudKeyVault]:
        """Validate VaultOptions and instantiate a CloudKeyVaultBitwarden adapter."""
        if not options.name or not options.name.strip():
            return fail("VaultOptions.name must not be blank.", "Validation")

        has_static = bool(
            (options.secrets.get("accessToken") or "").strip()
            or (options.secrets.get("machineAccessToken") or "").strip()
        )
        has_credential = bool((options.credential or "").strip()) and token_source is not None
        if not has_static and not has_credential:
            return fail(
                f"Vault '{options.name}' requires either key_vault.credential or secrets['accessToken'].",
                "Validation",
            )

        return ok(CloudKeyVaultBitwarden(options, token_source, self._runner))

    def authorization(self, options: VaultOptions) -> Result[OAuthOptions | None]:
        """Return OAuthOptions when interactive OAuth is configured for the Bitwarden vault."""
        auth_endpoint = (options.settings.get("authorizationEndpoint") or "").strip()
        token_endpoint = (options.settings.get("tokenEndpoint") or "").strip()
        client_id = (options.settings.get("clientId") or "").strip()

        if not auth_endpoint and not token_endpoint and not client_id:
            if not (options.credential or "").strip():
                return ok(None)
            return ok(
                OAuthOptions(
                    authorization_endpoint="https://identity.bitwarden.com/connect/authorize",
                    token_endpoint="https://identity.bitwarden.com/connect/token",
                    client_id="bitwarden-secrets-cli",
                )
            )

        return ok(
            OAuthOptions(
                authorization_endpoint=auth_endpoint or "https://identity.bitwarden.com/connect/authorize",
                token_endpoint=token_endpoint or "https://identity.bitwarden.com/connect/token",
                client_id=client_id or "bitwarden-secrets-cli",
            )
        )
