"""Unit tests for OwnerOnlyAccess."""

from __future__ import annotations

import os
from pathlib import Path
import sys

from nyssa_ai_logging.file_logging.owner_only_access import OwnerOnlyAccess
from test.unit.helpers.temp_log_workspace import TempLogWorkspace


def test_owner_only_create_directory_creates_safely() -> None:
    with TempLogWorkspace() as workspace:
        sub_dir = os.path.join(workspace.directory_path, "sub_logs")
        OwnerOnlyAccess.create_directory(sub_dir)
        assert os.path.exists(sub_dir)

        if sys.platform != "win32":
            mode = os.stat(sub_dir).st_mode & 0o777
            assert mode == 0o700


def test_owner_only_apply_to_file_sets_permissions_safely() -> None:
    with TempLogWorkspace() as workspace:
        test_file = os.path.join(workspace.directory_path, "owner.log")
        with open(test_file, "w", encoding="utf-8") as f:
            f.write("test data\n")

        OwnerOnlyAccess.apply_to_file(test_file)

        if sys.platform != "win32":
            mode = os.stat(test_file).st_mode & 0o777
            assert mode == 0o600
