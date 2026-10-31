"""Temporary workspace for log file tests."""

from __future__ import annotations

import json
import os
import shutil
import tempfile
import uuid


class TempLogWorkspace:
    """Creates isolated temp directory and parses written JSON log records."""

    def __init__(self, file_name: str = "application.log") -> None:
        """Create a temporary directory and resolve the log path."""
        self.directory_path = os.path.join(tempfile.gettempdir(), f"PyLogTest_{uuid.uuid4().hex}")
        os.makedirs(self.directory_path, exist_ok=True)
        self.log_path = os.path.join(self.directory_path, file_name)

    def read_records(self, file_path: str | None = None) -> list[dict[str, object]]:
        """Read and parse all JSON log records from the target log file."""
        target = file_path if file_path is not None else self.log_path
        if not os.path.exists(target):
            return []
        with open(target, encoding="utf-8") as f:
            lines = [line.strip() for line in f if line.strip()]
        return [json.loads(line) for line in lines]

    def dispose(self) -> None:
        """Clean up the temporary directory and all log files."""
        if os.path.exists(self.directory_path):
            shutil.rmtree(self.directory_path, ignore_errors=True)

    def __enter__(self) -> TempLogWorkspace:
        """Enter context manager."""
        return self

    def __exit__(self, exc_type: object, exc_val: object, exc_tb: object) -> None:
        """Exit context manager and clean up."""
        self.dispose()
