"""Context manager capturing standard output."""

from __future__ import annotations

import io
import sys
from types import TracebackType


class StdoutCapture:
    """Redirects and captures writes to sys.stdout."""

    def __init__(self) -> None:
        """Initialize capture buffer."""
        self._buffer = io.StringIO()
        self._original = sys.stdout

    def __enter__(self) -> StdoutCapture:
        """Begin capturing stdout."""
        sys.stdout = self._buffer
        return self

    def __exit__(
        self,
        exc_type: type[BaseException] | None,
        exc_val: BaseException | None,
        exc_tb: TracebackType | None,
    ) -> None:
        """Restore original sys.stdout."""
        self.dispose()

    @property
    def text(self) -> str:
        """Return captured text."""
        return self._buffer.getvalue()

    def dispose(self) -> None:
        """Restore original sys.stdout."""
        if sys.stdout is self._buffer:
            sys.stdout = self._original
