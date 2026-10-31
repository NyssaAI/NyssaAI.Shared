# Responsibility: Define the BrowserLauncher protocol for opening interactive OAuth authorization URLs.
# Public interface:
# `-- BrowserLauncher
#     `-- open_url(url)
"""Protocol for launching the user's browser during interactive OAuth PKCE flows."""

from typing import Protocol

from nyssa_ai_configuration.results.result import Result


class BrowserLauncher(Protocol):
    """Opens an HTTPS authorization URL in the user's system browser."""

    def open_url(self, url: str) -> Result[None]:
        """Launch the supplied HTTPS URL and return Ok(None) or Err."""
        ...
