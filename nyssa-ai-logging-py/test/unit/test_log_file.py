"""Unit tests for LogFile initialization and appending."""

from __future__ import annotations

import os

from nyssa_ai_logging.file_logging.log_file import LogFile
from nyssa_ai_logging.file_logging.log_record import LogRecord
from test.unit.helpers.temp_log_workspace import TempLogWorkspace


def _create_record(message: str) -> LogRecord:
    return LogRecord(
        Timestamp="2026-10-13T12:00:00.000Z",
        Level="Information",
        Message=message,
        Module="TestModule",
        Operation="TestOp",
        Exception=None,
        AdditionalFields={},
    )


def test_log_file_initialize_valid_path_creates_directory_and_opens_file() -> None:
    with TempLogWorkspace() as workspace:
        log_file = LogFile(workspace.log_path)
        err = log_file.initialize()
        assert err is None
        assert os.path.exists(workspace.log_path)
        log_file.dispose()


def test_log_file_initialize_invalid_path_returns_error_without_throwing() -> None:
    log_file = LogFile("relative.log")
    err = log_file.initialize()
    assert err is not None
    assert "Invalid log path" in err
    log_file.dispose()


def test_log_file_write_record_appends_to_disk_immediately() -> None:
    with TempLogWorkspace() as workspace:
        log_file = LogFile(workspace.log_path)
        assert log_file.initialize() is None

        log_file.write_record(_create_record("First record"))
        log_file.dispose()

        records = workspace.read_records()
        assert len(records) == 1
        assert records[0]["Message"] == "First record"
