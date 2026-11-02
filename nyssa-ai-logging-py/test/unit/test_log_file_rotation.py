"""Unit tests for LogFile size rotation and backup retention."""

from __future__ import annotations

from datetime import UTC, datetime, timedelta
import os
import pytest

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


def _count_rotation_failures(stderr_text: str) -> int:
    return sum(1 for line in stderr_text.splitlines() if "failed to rotate log file" in line)


def _count_reopen_failures(stderr_text: str) -> int:
    return sum(1 for line in stderr_text.splitlines() if "failed to reopen log file:" in line)


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


@pytest.mark.parametrize(("trigger_bytes", "expected_backups"), [(64, 1), (1024 * 1024, 0)])
def test_write_record_rotates_only_once_file_passes_trigger(
    trigger_bytes: int, expected_backups: int
) -> None:
    with TempLogWorkspace() as workspace:
        log = LogFile(workspace.log_path, rotation_trigger_bytes=trigger_bytes)
        assert log.initialize() is None

        log.write_record(_create_record("first"))
        log.write_record(_create_record("second"))
        log.dispose()

        assert _count_backups(workspace.log_path) == expected_backups


def test_write_record_when_rotation_fails_reports_once_then_backs_off_by_one_trigger(
    capsys: pytest.CaptureFixture[str],
) -> None:
    trigger = 1024
    with TempLogWorkspace() as workspace:
        os.makedirs(f"{workspace.log_path}.1", exist_ok=True)
        log = LogFile(workspace.log_path, rotation_trigger_bytes=trigger)
        assert log.initialize() is None
        big = "x" * (trigger + 100)

        log.write_record(_create_record(big))  # over the trigger, no attempt yet
        log.write_record(_create_record("second"))  # attempt 1 fails; back off one trigger
        log.write_record(_create_record("third"))  # inside the back-off window
        log.write_record(_create_record("fourth"))  # still inside

        stderr_mid = capsys.readouterr().err
        assert _count_rotation_failures(stderr_mid) == 1

        log.write_record(_create_record(big))  # pushes past the retry threshold
        log.write_record(_create_record("sixth"))  # attempt 2
        log.dispose()

        stderr_end = capsys.readouterr().err
        assert _count_rotation_failures(stderr_mid + stderr_end) == 2

        with open(workspace.log_path, encoding="utf-8") as f:
            content = f.read()
        for message in ("second", "third", "fourth", "sixth"):
            assert message in content


def test_write_record_when_rotation_and_reopen_fail_resumes_after_access_is_restored(
    capsys: pytest.CaptureFixture[str],
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    clock = FixedClock(datetime(2026, 10, 13, 12, 0, 0, tzinfo=UTC))
    with TempLogWorkspace() as workspace:
        log = LogFile(workspace.log_path, rotation_trigger_bytes=64, clock=clock)
        assert log.initialize() is None
        log.write_record(_create_record("before outage"))

        displaced_path = f"{workspace.log_path}.displaced"
        real_rename = os.rename

        def displace_and_block(src: str, dst: str) -> None:
            if src == workspace.log_path and dst == f"{workspace.log_path}.1":
                real_rename(workspace.log_path, displaced_path)
                os.makedirs(workspace.log_path, exist_ok=True)
                raise OSError("Simulated rotation failure with directory blocking active path")
            real_rename(src, dst)

        monkeypatch.setattr(os, "rename", displace_and_block)

        log.write_record(_create_record("during outage"))
        stderr1 = capsys.readouterr().err
        assert _count_rotation_failures(stderr1) == 1
        assert _count_reopen_failures(stderr1) == 1

        for _ in range(10):
            log.write_record(_create_record("still unavailable"))

        stderr2 = capsys.readouterr().err
        assert _count_reopen_failures(stderr1 + stderr2) == 1

        clock.advance(timedelta(minutes=1))
        log.write_record(_create_record("retry while unavailable"))
        stderr3 = capsys.readouterr().err
        assert _count_reopen_failures(stderr1 + stderr2 + stderr3) == 2

        monkeypatch.setattr(os, "rename", real_rename)
        os.rmdir(workspace.log_path)
        real_rename(displaced_path, workspace.log_path)
        clock.advance(timedelta(minutes=1))
        log.write_record(_create_record("after recovery"))
        log.dispose()

        stderr4 = capsys.readouterr().err
        assert _count_reopen_failures(stderr1 + stderr2 + stderr3 + stderr4) == 2

        with open(workspace.log_path, encoding="utf-8") as f:
            content = f.read()
        assert "before outage" in content
        assert "after recovery" in content
        assert "during outage" not in content


def test_write_record_when_disposed_after_rotation_and_reopen_fail_does_not_reopen(
    capsys: pytest.CaptureFixture[str],
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    clock = FixedClock(datetime(2026, 10, 13, 12, 0, 0, tzinfo=UTC))
    with TempLogWorkspace() as workspace:
        log = LogFile(workspace.log_path, rotation_trigger_bytes=64, clock=clock)
        assert log.initialize() is None
        log.write_record(_create_record("before outage"))

        displaced_path = f"{workspace.log_path}.displaced"
        real_rename = os.rename

        def displace_and_block(src: str, dst: str) -> None:
            if src == workspace.log_path and dst == f"{workspace.log_path}.1":
                real_rename(workspace.log_path, displaced_path)
                os.makedirs(workspace.log_path, exist_ok=True)
                raise OSError("Simulated rotation failure")
            real_rename(src, dst)

        monkeypatch.setattr(os, "rename", displace_and_block)

        log.write_record(_create_record("during outage"))
        stderr1 = capsys.readouterr().err
        assert _count_reopen_failures(stderr1) == 1
        log.dispose()

        monkeypatch.setattr(os, "rename", real_rename)
        os.rmdir(workspace.log_path)
        clock.advance(timedelta(minutes=1))
        log.write_record(_create_record("after disposal"))

        stderr2 = capsys.readouterr().err
        assert not os.path.exists(workspace.log_path)
        assert _count_reopen_failures(stderr1 + stderr2) == 1


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
