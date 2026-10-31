# Responsibility: Define the ConfigurationCipher protocol for address-bound secret encryption.
# Public interface:
# `-- ConfigurationCipher
#     +-- open(value, namespace, key)
#     `-- seal(value, namespace, key)
"""Protocol for sealing and opening address-bound configuration secrets."""

from typing import Protocol

from nyssa_ai_configuration.results.result import Result


class ConfigurationCipher(Protocol):
    """Seals and opens configuration values bound to (namespace, key) associated data."""

    def seal(self, value: str, namespace: str, key: str) -> Result[str]:
        """Encrypt value bound to (namespace, key) and return an unpadded Base64URL string."""
        ...

    def open(self, value: str, namespace: str, key: str) -> Result[str]:
        """Authenticate and decrypt an unpadded Base64URL payload bound to (namespace, key)."""
        ...
