"""Unit tests for LogFile initialization and appending."""

from __future__ import annotations

from datetime import UTC, datetime, timedelta
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


def test_initialize_with_second_writer_on_same_path_both_succeed() -> None:
    with TempLogWorkspace() as workspace:
        first = LogFile(workspace.log_path)
        second = LogFile(workspace.log_path)
        try:
            assert first.initialize() is None
            assert second.initialize() is None

            first.write_record(_create_record("from the first writer"))
            second.write_record(_create_record("from the second writer"))
        finally:
            first.dispose()
            second.dispose()

        with open(workspace.log_path, encoding="utf-8") as f:
            content = f.read()
        assert "from the first writer" in content
        assert "from the second writer" in content


def test_initialize_with_expired_backup_deletes_it_and_keeps_fresh_one() -> None:
    with TempLogWorkspace() as workspace:
        expired = f"{workspace.log_path}.2"
        fresh = f"{workspace.log_path}.1"
        with open(expired, "w", encoding="utf-8") as f:
            f.write("old")
        old_epoch = (datetime.now(UTC) - timedelta(days=LogFile.RETENTION_DAYS + 1)).timestamp()
        os.utime(expired, (old_epoch, old_epoch))
        with open(fresh, "w", encoding="utf-8") as f:
            f.write("recent")

        log_file = LogFile(workspace.log_path)
        try:
            assert log_file.initialize() is None
            assert not os.path.exists(expired)
            assert os.path.exists(fresh)
        finally:
            log_file.dispose()


def test_write_record_with_structured_fields_and_exception() -> None:
    with TempLogWorkspace() as workspace:
        log_file = LogFile(workspace.log_path)
        assert log_file.initialize() is None

        log_file.write_record(
            LogRecord(
                Timestamp="2026-10-13T12:00:00.000Z",
                Level="Error",
                Message="Operation failed",
                Module="TestModule",
                Operation="TestOp",
                Exception="RuntimeError: Test error",
                AdditionalFields={"userId": 123, "action": "login"},
            )
        )
        log_file.dispose()

        with open(workspace.log_path, encoding="utf-8") as f:
            content = f.read()
        assert "Operation failed" in content
        assert "RuntimeError: Test error" in content
        assert "userId" in content
        assert "login" in content


def test_write_record_before_initialize_does_nothing() -> None:
    with TempLogWorkspace() as workspace:
        log_file = LogFile(workspace.log_path)
        try:
            log_file.write_record(_create_record("before initialize"))
            assert not os.path.exists(workspace.log_path)
        finally:
            log_file.dispose()


def test_write_record_after_dispose_is_ignored_without_throwing() -> None:
    with TempLogWorkspace() as workspace:
        log_file = LogFile(workspace.log_path)
        assert log_file.initialize() is None
        log_file.write_record(_create_record("before disposal"))
        log_file.dispose()

        log_file.write_record(_create_record("after disposal"))

        with open(workspace.log_path, encoding="utf-8") as f:
            content = f.read()
        assert "before disposal" in content
        assert "after disposal" not in content


def test_dispose_called_twice_releases_the_file_and_keeps_what_was_written() -> None:
    with TempLogWorkspace() as workspace:
        log_file = LogFile(workspace.log_path)
        assert log_file.initialize() is None
        log_file.write_record(_create_record("written before dispose"))

        log_file.dispose()
        log_file.dispose()

        with open(workspace.log_path, encoding="utf-8") as f:
            content = f.read()
        assert "written before dispose" in content
        os.remove(workspace.log_path)
        assert not os.path.exists(workspace.log_path)
