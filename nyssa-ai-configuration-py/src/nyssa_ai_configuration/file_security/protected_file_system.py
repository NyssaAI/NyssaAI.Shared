# Responsibility: Create directories and atomically publish files protected to the current user account.
# Public interface:
# `-- ProtectedFileSystem
#     +-- create_directory(directory_path)
#     `-- create_file(file_path, content)
"""Protected directory creation and atomic file replacement across POSIX and Windows."""

import os
from pathlib import Path
import subprocess
import sys
from typing import Final
import uuid

from nyssa_ai_configuration.results.result import Result, fail, ok

_POSIX_DIR_MODE: Final[int] = 0o700
_POSIX_FILE_MODE: Final[int] = 0o600


class ProtectedFileSystem:
    """Creates user-private directories (0700) and files (0600) with atomic temp-file replacement."""

    @staticmethod
    def create_directory(directory_path: str | Path) -> Result[Path]:
        """Create a directory restricted to the current user account."""
        text = str(directory_path).strip()
        if not text:
            return fail("Directory path cannot be blank.", "Validation")
        try:
            resolved = Path(text).resolve()
            if not resolved.exists():
                resolved.mkdir(parents=True, exist_ok=True, mode=_POSIX_DIR_MODE)
                if sys.platform == "win32":
                    ProtectedFileSystem._protect_windows_path(resolved, is_directory=True)
                else:
                    try:
                        resolved.chmod(_POSIX_DIR_MODE)
                    except OSError:
                        pass
            return ok(resolved)
        except Exception as exc:
            return fail(f"Failed to create protected directory '{directory_path}': {exc}", "Forbidden", exc)

    @staticmethod
    def create_file(file_path: str | Path, content: str) -> Result[Path]:
        """Atomically write a file restricted to the current user account via a sibling temp file."""
        text = str(file_path).strip()
        if not text:
            return fail("File path cannot be blank.", "Validation")
        try:
            resolved = Path(text).resolve()
            dir_res = ProtectedFileSystem.create_directory(resolved.parent)
            if not dir_res.ok:
                return dir_res

            temp_path = resolved.with_name(f".{resolved.name}.{uuid.uuid4().hex}.tmp")
            fd = os.open(temp_path, os.O_CREAT | os.O_WRONLY | os.O_TRUNC, _POSIX_FILE_MODE)
            try:
                data = content.encode("utf-8")
                os.write(fd, data)
                os.fsync(fd)
            finally:
                os.close(fd)

            if sys.platform == "win32":
                ProtectedFileSystem._protect_windows_path(temp_path, is_directory=False)
            else:
                try:
                    temp_path.chmod(_POSIX_FILE_MODE)
                except OSError:
                    pass

            os.replace(temp_path, resolved)
            return ok(resolved)
        except Exception as exc:
            return fail(f"Failed to write protected file '{file_path}': {exc}", "Forbidden", exc)

    @staticmethod
    def _protect_windows_path(target_path: Path, is_directory: bool) -> None:
        try:
            username = os.environ.get("USERNAME") or os.environ.get("USER")
            if not username:
                return
            grant_flag = "(OI)(CI)F" if is_directory else "F"
            subprocess.run(
                [
                    "icacls.exe",
                    str(target_path),
                    "/inheritance:r",
                    "/grant:r",
                    f"{username}:{grant_flag}",
                    "/grant:r",
                    f"SYSTEM:{grant_flag}",
                    "/grant:r",
                    f"Administrators:{grant_flag}",
                ],
                stdout=subprocess.DEVNULL,
                stderr=subprocess.DEVNULL,
                check=False,
            )
        except Exception:
            pass
