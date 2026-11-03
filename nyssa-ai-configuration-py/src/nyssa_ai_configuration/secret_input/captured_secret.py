# Responsibility: Disposable container for a captured plaintext secret that wipes its buffer on close.
# Public interface:
# `-- CapturedSecret
#     +-- close()
#     +-- new(value, selected_provider)
#     +-- selected_provider
#     `-- value
"""Disposable container for a captured secret."""

from __future__ import annotations


class CapturedSecret:
    """Disposable container for a captured plaintext secret that wipes its buffer on close."""

    def __init__(self, value: str, selected_provider: str) -> None:
        self._buffer = bytearray(value.encode("utf-8"))
        self._selected_provider = selected_provider
        self._closed = False

    @property
    def value(self) -> str:
        """Return the secret string."""
        return self._buffer.decode("utf-8")

    @property
    def selected_provider(self) -> str:
        """Return the destination provider chosen."""
        return self._selected_provider

    def close(self) -> None:
        """Zero out the byte buffer immediately."""
        if not self._closed:
            for i in range(len(self._buffer)):
                self._buffer[i] = 0
            self._closed = True

    def __enter__(self) -> CapturedSecret:
        return self

    def __exit__(self, exc_type: object, exc_val: object, exc_tb: object) -> None:
        self.close()
