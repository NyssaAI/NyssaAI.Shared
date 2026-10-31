# Responsibility: Verify FileLock persistent lock-file creation, cross-thread serialization, and timeout diagnostics.
# Public interface:
# +-- test_file_lock_persists_lock_file_and_serializes_threads(tmp_path)
# `-- test_file_lock_times_out_and_logs_degraded_warning_when_held(tmp_path)
"""Unit tests for FileLock concurrency and lock-file lifecycle."""

import json
from pathlib import Path
import threading

from nyssa_ai_configuration.file_security.file_lock import FileLock
from nyssa_ai_configuration.logging.configuration_log import ConfigurationLog


def test_file_lock_persists_lock_file_and_serializes_threads(tmp_path: Path) -> None:
    """Verify FileLock keeps the .lock file on disk after release and serializes concurrent threads."""
    target_file = tmp_path / "locks" / "resource.json"
    events: list[str] = []
    first_entered = threading.Event()
    release_first = threading.Event()

    def hold_first() -> None:
        lock = FileLock(target_file, timeout_ms=2000)
        res = lock.acquire()
        assert res.ok
        try:
            events.append("first-acquired")
            first_entered.set()
            release_first.wait(timeout=2.0)
            events.append("first-releasing")
        finally:
            lock.release()

    worker = threading.Thread(target=hold_first)
    worker.start()
    assert first_entered.wait(timeout=2.0)

    release_first.set()
    second_lock = FileLock(target_file, timeout_ms=2000)
    second_res = second_lock.acquire()
    assert second_res.ok
    try:
        events.append("second-acquired")
    finally:
        second_lock.release()

    worker.join(timeout=2.0)
    assert events == ["first-acquired", "first-releasing", "second-acquired"]
    assert Path(f"{target_file}.lock").exists()


def test_file_lock_times_out_and_logs_degraded_warning_when_held(tmp_path: Path) -> None:
    """Verify FileLock returns Unavailable on timeout and emits a degraded operational log."""
    target_file = tmp_path / "contended.json"
    log_lines: list[str] = []
    logger = ConfigurationLog(sink=log_lines.append)

    first_lock = FileLock(target_file, timeout_ms=1000, logger=logger)
    first_res = first_lock.acquire()
    assert first_res.ok
    try:
        second_lock = FileLock(target_file, timeout_ms=80, logger=logger)
        second_res = second_lock.acquire()
        assert not second_res.ok
        assert second_res.category == "Unavailable"
    finally:
        first_lock.release()

    records = [json.loads(line) for line in log_lines if line.strip()]
    assert any(r["level"] == "warn" and r["operation"] == "file_lock.acquire" for r in records)
    assert any(r.get("outcome") == "failure" and r.get("code") == "lock.timeout" for r in records)
