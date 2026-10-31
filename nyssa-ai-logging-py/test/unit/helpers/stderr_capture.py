"""Context manager capturing standard error output."""

from __future__ import annotations

import io
import sys
from types import TracebackType


class StderrCapture:
    """Redirects and captures writes to sys.stderr."""

    def __init__(self) -> None:
        """Initialize capture buffer."""
        self._buffer = io.StringIO()
        self._original = sys.stderr

    def __enter__(self) -> StderrCapture:
        """Begin capturing stderr."""
        sys.stderr = self._buffer
        return self

    def __exit__(
        self,
        exc_type: type[BaseException] | None,
        exc_val: BaseException | None,
        exc_tb: TracebackType | None,
    ) -> None:
        """Restore original sys.stderr."""
        self.dispose()

    @property
    def text(self) -> str:
        """Return captured text."""
        return self._buffer.getvalue()

    def dispose(self) -> None:
        """Restore original sys.stderr."""
        if sys.stderr is self._buffer:
            sys.stderr = self._original
