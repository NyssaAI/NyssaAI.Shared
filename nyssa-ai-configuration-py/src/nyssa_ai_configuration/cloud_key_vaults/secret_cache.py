# Responsibility: Define the SecretCache protocol for pluggable in-memory or cross-process vault secret caching.
# Public interface:
# `-- SecretCache
#     +-- clear()
#     +-- get(key)
#     +-- remove(key)
#     `-- set(key, value, ttl_seconds)
"""Protocol for caching resolved cloud key vault secrets with a time-to-live (TTL)."""

from typing import Protocol


class SecretCache(Protocol):
    """Pluggable cache store for resolved vault secrets (in-memory or OS keyring)."""

    def get(self, key: str) -> str | None:
        """Return the cached cleartext secret for key if present and unexpired, else None."""
        ...

    def set(self, key: str, value: str, ttl_seconds: int) -> None:
        """Store value under key for ttl_seconds."""
        ...

    def remove(self, key: str) -> None:
        """Evict key from the cache if present."""
        ...

    def clear(self) -> None:
        """Evict all cached entries."""
        ...
