# Responsibility: Define the AccessTokenSource protocol for supplying bearer credentials to cloud key vaults.
# Public interface:
# `-- AccessTokenSource
#     `-- get_access_token()
"""Protocol for resolving bearer access tokens from ConfigurationStore."""

from typing import Protocol

from nyssa_ai_configuration.results.result import Result


class AccessTokenSource(Protocol):
    """Supplies current bearer access tokens to an external cloud key vault adapter."""

    def get_access_token(self) -> Result[str]:
        """Resolve and return a current bearer access token (refreshing OAuth grants if needed)."""
        ...
