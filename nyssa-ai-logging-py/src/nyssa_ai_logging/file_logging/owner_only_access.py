# Responsibility: Enforce owner-only access controls on library-created log directories and files.
# Public interface:
# `-- OwnerOnlyAccess
#     +-- OWNER_READ_WRITE
#     +-- OWNER_READ_WRITE_EXECUTE
#     +-- apply_to_file(file_path)
#     `-- create_directory(directory_path)
"""Filesystem access controls for log files and directories."""

from __future__ import annotations

import os
from pathlib import Path
import subprocess
import sys

from nyssa_ai_logging.file_logging.stderr_diagnostics import StderrDiagnostics


class OwnerOnlyAccess:
    """Enforces owner-only permissions on log files and directories."""

    OWNER_READ_WRITE = 0o600
    OWNER_READ_WRITE_EXECUTE = 0o700

    @staticmethod
    def create_directory(directory_path: str) -> None:
        """Create the log directory restricted to the owner."""
        p = Path(directory_path)
        if p.exists():
            return

        try:
            if sys.platform == "win32":
                p.mkdir(parents=True, exist_ok=True)
                OwnerOnlyAccess._apply_windows_acl(str(p), is_directory=True)
                return

            p.mkdir(parents=True, exist_ok=True, mode=OwnerOnlyAccess.OWNER_READ_WRITE_EXECUTE)
            OwnerOnlyAccess._restrict_unix_path(str(p), OwnerOnlyAccess.OWNER_READ_WRITE_EXECUTE)
        except Exception as ex:
            StderrDiagnostics.warning(
                directory_path, f"failed to restrict directory permissions: {ex}"
            )

    @staticmethod
    def apply_to_file(file_path: str) -> None:
        """Restrict an existing log file to the owner."""
        try:
            if sys.platform == "win32":
                OwnerOnlyAccess._apply_windows_acl(file_path, is_directory=False)
                return

            OwnerOnlyAccess._restrict_unix_path(file_path, OwnerOnlyAccess.OWNER_READ_WRITE)
        except Exception as ex:
            StderrDiagnostics.warning(file_path, f"failed to restrict file permissions: {ex}")

    @staticmethod
    def _restrict_unix_path(target_path: str, mode: int) -> None:
        try:
            os.chmod(target_path, mode)
        except Exception as ex:
            StderrDiagnostics.warning(target_path, f"failed to restrict permissions: {ex}")

    @staticmethod
    def _apply_windows_acl(target_path: str, is_directory: bool) -> None:
        try:
            username = os.environ.get("USERNAME") or os.environ.get("USER")
            if not username:
                return

            flag = "(OI)(CI)F" if is_directory else "F"
            subprocess.run(
                [
                    "icacls.exe",
                    target_path,
                    "/inheritance:r",
                    "/grant:r",
                    f"{username}:{flag}",
                    "/grant:r",
                    f"SYSTEM:{flag}",
                    "/grant:r",
                    f"Administrators:{flag}",
                ],
                stdout=subprocess.DEVNULL,
                stderr=subprocess.DEVNULL,
                check=False,
            )
        except Exception:
            # Best-effort on Windows
            pass
