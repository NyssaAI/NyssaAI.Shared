# Responsibility: Thread-safe in-memory SecretCache implementation with monotonic TTL expiration.
# Public interface:
# `-- SecretCacheInMemory
#     +-- clear()
#     +-- get(key)
#     +-- new(clock)
#     +-- remove(key)
#     `-- set(key, value, ttl_seconds)
"""In-memory dictionary implementation of SecretCache using an injected monotonic clock."""

from collections.abc import Callable
import threading
import time
from typing import final


@final
class SecretCacheInMemory:
    """In-memory SecretCache backed by a lock-protected dictionary and monotonic clock."""

    def __init__(self, clock: Callable[[], float] = time.monotonic) -> None:
        """Initialize empty cache with the supplied monotonic clock (in seconds)."""
        self._clock = clock
        self._lock = threading.Lock()
        self._entries: dict[str, tuple[str, float]] = {}

    def get(self, key: str) -> str | None:
        """Return the cached secret for key when present and not expired, otherwise None."""
        now = self._clock()
        with self._lock:
            entry = self._entries.get(key)
            if entry is None:
                return None
            value, expires_at = entry
            if now >= expires_at:
                del self._entries[key]
                return None
            return value

    def set(self, key: str, value: str, ttl_seconds: int) -> None:
        """Store value under key for ttl_seconds when ttl_seconds > 0."""
        if ttl_seconds <= 0:
            self.remove(key)
            return
        expires_at = self._clock() + float(ttl_seconds)
        with self._lock:
            self._entries[key] = (value, expires_at)

    def remove(self, key: str) -> None:
        """Evict key from the in-memory cache."""
        with self._lock:
            self._entries.pop(key, None)

    def clear(self) -> None:
        """Evict all entries from the in-memory cache."""
        with self._lock:
            self._entries.clear()
