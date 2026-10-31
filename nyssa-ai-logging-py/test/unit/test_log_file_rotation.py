"""Unit tests for LogFile size rotation and backup retention."""

from __future__ import annotations

from datetime import UTC, datetime
import os

from nyssa_ai_logging.file_logging.log_file import LogFile
from nyssa_ai_logging.file_logging.log_record import LogRecord
from test.unit.helpers.fixed_clock import FixedClock
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


def _count_backups(base_path: str) -> int:
    directory = os.path.dirname(base_path)
    base_name = os.path.basename(base_path)
    count = 0
    for index in range(1, LogFile.BACKUP_RETENTION + 5):
        if os.path.exists(os.path.join(directory, f"{base_name}.{index}")):
            count += 1
    return count


def test_rotation_passes_trigger_archives_and_starts_new() -> None:
    with TempLogWorkspace() as workspace:
        log = LogFile(workspace.log_path, rotation_trigger_bytes=64)
        assert log.initialize() is None

        log.write_record(_create_record("first record"))
        log.write_record(_create_record("second record"))
        log.dispose()

        archived = f"{workspace.log_path}.1"
        assert os.path.exists(archived)

        with open(archived, encoding="utf-8") as f:
            archived_content = f.read()
        with open(workspace.log_path, encoding="utf-8") as f:
            active_content = f.read()

        assert "first record" in archived_content
        assert "second record" in active_content
        assert "first record" not in active_content


def test_rotation_repeats_shifts_backups_oldest_last() -> None:
    with TempLogWorkspace() as workspace:
        log = LogFile(workspace.log_path, rotation_trigger_bytes=64)
        assert log.initialize() is None

        log.write_record(_create_record("oldest"))
        log.write_record(_create_record("middle"))
        log.write_record(_create_record("newest"))
        log.dispose()

        with open(f"{workspace.log_path}.2", encoding="utf-8") as f:
            b2 = f.read()
        with open(f"{workspace.log_path}.1", encoding="utf-8") as f:
            b1 = f.read()
        with open(workspace.log_path, encoding="utf-8") as f:
            act = f.read()

        assert "oldest" in b2
        assert "middle" in b1
        assert "newest" in act


def test_rotation_accumulates_backups_up_to_retention() -> None:
    with TempLogWorkspace() as workspace:
        log = LogFile(workspace.log_path, rotation_trigger_bytes=64)
        assert log.initialize() is None

        for i in range(LogFile.BACKUP_RETENTION + 3):
            log.write_record(_create_record(f"entry {i}"))
        log.dispose()

        assert _count_backups(workspace.log_path) == LogFile.BACKUP_RETENTION
        assert not os.path.exists(f"{workspace.log_path}.{LogFile.BACKUP_RETENTION + 1}")


def test_rotation_prunes_backups_older_than_30_days() -> None:
    clock = FixedClock(datetime(2026, 10, 13, 12, 0, 0, tzinfo=UTC))
    with TempLogWorkspace() as workspace:
        log = LogFile(workspace.log_path, rotation_trigger_bytes=64, clock=clock)
        assert log.initialize() is None

        log.write_record(_create_record("old backup"))
        log.write_record(_create_record("active entry"))
        log.dispose()

        backup1 = f"{workspace.log_path}.1"
        assert os.path.exists(backup1)

        # Set file time to 31 days ago
        old_epoch = (datetime(2026, 9, 10, 12, 0, 0, tzinfo=UTC)).timestamp()
        os.utime(backup1, (old_epoch, old_epoch))

        # Advance clock 31 days
        clock.advance_days(31)

        next_log = LogFile(workspace.log_path, rotation_trigger_bytes=64, clock=clock)
        assert next_log.initialize() is None
        next_log.dispose()

        assert not os.path.exists(backup1)
