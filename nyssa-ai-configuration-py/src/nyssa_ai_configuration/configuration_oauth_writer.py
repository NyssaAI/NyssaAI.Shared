# Responsibility: Define the ConfigurationOauthWriter protocol for interactive OAuth 2.0 PKCE credential acquisition and renewal.
# Public interface:
# `-- ConfigurationOauthWriter
#     +-- authorize(namespace, key, options, browser_launcher)
#     +-- authorize_provider(provider_name, browser_launcher)
#     `-- reauthorize(namespace, key, browser_launcher)
"""OAuth credential acquisition and renewal protocol."""

from typing import Protocol

from nyssa_ai_configuration.oauth.browser_launcher import BrowserLauncher
from nyssa_ai_configuration.oauth.types import OAuthOptions
from nyssa_ai_configuration.results.result import Result


class ConfigurationOauthWriter(Protocol):
    """Interface for interactive OAuth 2.0 PKCE credential sign-in and renewal."""

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
