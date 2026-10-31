# Responsibility: Manage active log file, synchronous appending, size rotation, and retention pruning.
# Public interface:
# `-- LogFile
#     +-- BACKUP_RETENTION
#     +-- RETENTION_DAYS
#     +-- ROTATION_TRIGGER_MB
#     +-- dispose()
#     +-- initialize()
#     +-- new(log_path, rotation_trigger_bytes, clock)
#     `-- write_record(record)
"""Log file management with size rotation and retention pruning."""

from __future__ import annotations

from datetime import timedelta
import io
import os
from pathlib import Path
import sys
import threading
from typing import TextIO

from nyssa_ai_logging.file_logging.clock import Clock
from nyssa_ai_logging.file_logging.system_clock import SystemClock
from nyssa_ai_logging.file_logging.log_record import LogRecord
from nyssa_ai_logging.file_logging.log_record_serializer import LogRecordSerializer
from nyssa_ai_logging.file_logging.owner_only_access import OwnerOnlyAccess
from nyssa_ai_logging.file_logging.stderr_diagnostics import StderrDiagnostics

_BYTES_PER_MEGABYTE = 1024 * 1024
_REOPEN_RETRY_DELAY = timedelta(seconds=1)


class LogFile:
    """Manages appending, size rotation, and backup retention for one log file."""

    ROTATION_TRIGGER_MB = 10
    BACKUP_RETENTION = 5
    RETENTION_DAYS = 30

    def __init__(
        self,
        log_path: str,
        rotation_trigger_bytes: int | None = None,
        clock: Clock | None = None,
    ) -> None:
        """Initialize with target path, optional byte rotation trigger, and clock."""
        self._log_path = log_path
        self._rotation_trigger_bytes = (
            rotation_trigger_bytes
            if rotation_trigger_bytes is not None
            else self.ROTATION_TRIGGER_MB * _BYTES_PER_MEGABYTE
        )
        self._clock = clock if clock is not None else SystemClock()
        self._lock = threading.Lock()
        self._stream: TextIO | None = None
        self._bytes_written = 0
        self._rotation_retry_at_bytes = 0
        self._reopen_retry_at: object | None = None
        self._disposed = False

    def initialize(self) -> str | None:
        """Create directory and file, open for appending, and prune backups. Return error string or None."""
        try:
            directory_path = os.path.dirname(self._log_path)
            if not directory_path or directory_path == ".":
                return f"Invalid log path: {self._log_path}"

            OwnerOnlyAccess.create_directory(directory_path)

            with self._lock:
                self._stream = self._open_append(self._log_path)
                self._bytes_written = os.fstat(self._stream.fileno()).st_size

                OwnerOnlyAccess.apply_to_file(self._log_path)
                self._delete_expired_backups()

            return None
        except Exception as ex:
            return f"Failed to initialize log file: {ex}"

    def write_record(self, record: LogRecord) -> None:
        """Serialize and append record to active file, rotating first if threshold passed."""
        with self._lock:
            if self._disposed:
                return

            try:
                if self._stream is None:
                    now = self._clock.now()
                    if self._reopen_retry_at is not None and isinstance(self._reopen_retry_at, type(now)):
                        if now < self._reopen_retry_at:
                            return

                    self._try_reopen_after_failed_rotation()
                    if self._stream is None:
                        return

                if (
                    self._bytes_written > self._rotation_trigger_bytes
                    and self._bytes_written >= self._rotation_retry_at_bytes
                ):
                    self._rotate()

                if self._stream is None:
                    return

                line = LogRecordSerializer.serialize(record)
                self._stream.write(line + "\n")
                self._stream.flush()
                try:
                    os.fsync(self._stream.fileno())
                except (OSError, io.UnsupportedOperation):
                    pass

                self._bytes_written = os.fstat(self._stream.fileno()).st_size
            except Exception as ex:
                StderrDiagnostics.error(self._log_path, f"failed to write log record: {ex}")

    def dispose(self) -> None:
        """Close and release the active file stream."""
        with self._lock:
            if self._disposed:
                return
            self._disposed = True
            if self._stream is not None:
                try:
                    self._stream.close()
                except Exception:
                    pass
                self._stream = None

    def _open_append(self, path: str) -> TextIO:
        if sys.platform != "win32":
            fd = os.open(
                path,
                os.O_WRONLY | os.O_CREAT | os.O_APPEND,
                OwnerOnlyAccess.OWNER_READ_WRITE,
            )
            return open(fd, "a", encoding="utf-8")
        return open(path, "a", encoding="utf-8")

    def _rotate(self) -> None:
        try:
            if self._stream is not None:
                self._stream.close()
                self._stream = None

            for index in range(self.BACKUP_RETENTION - 1, 0, -1):
                older_path = f"{self._log_path}.{index}"
                newer_path = f"{self._log_path}.{index + 1}"

                if not os.path.exists(older_path):
                    continue

                if os.path.exists(newer_path):
                    os.remove(newer_path)

                os.rename(older_path, newer_path)

            archive_path = f"{self._log_path}.1"
            if os.path.exists(archive_path):
                os.remove(archive_path)

            if os.path.exists(self._log_path):
                os.rename(self._log_path, archive_path)

            self._delete_expired_backups()

            self._stream = self._open_append(self._log_path)
            OwnerOnlyAccess.apply_to_file(self._log_path)
            self._bytes_written = 0
            self._rotation_retry_at_bytes = 0
        except Exception as ex:
            StderrDiagnostics.error(self._log_path, f"failed to rotate log file: {ex}")
            self._try_reopen_after_failed_rotation()

    def _delete_expired_backups(self) -> None:
        directory = os.path.dirname(self._log_path)
        if not directory or not os.path.exists(directory):
            return

        base_name = os.path.basename(self._log_path)
        cutoff_timestamp = (self._clock.now() - timedelta(days=self.RETENTION_DAYS)).timestamp()

        for index in range(1, self.BACKUP_RETENTION + 1):
            backup_path = os.path.join(directory, f"{base_name}.{index}")
            if not os.path.exists(backup_path):
                continue

            try:
                mtime = os.path.getmtime(backup_path)
                if mtime < cutoff_timestamp:
                    os.remove(backup_path)
            except Exception as ex:
                StderrDiagnostics.warning(backup_path, f"failed to prune expired backup: {ex}")

    def _try_reopen_after_failed_rotation(self) -> None:
        try:
            self._stream = self._open_append(self._log_path)
            self._bytes_written = os.fstat(self._stream.fileno()).st_size
            self._rotation_retry_at_bytes = self._bytes_written + _BYTES_PER_MEGABYTE
            self._reopen_retry_at = None
        except Exception as ex:
            self._reopen_retry_at = self._clock.now() + _REOPEN_RETRY_DELAY
            StderrDiagnostics.error(
                self._log_path, f"failed to reopen log file after failed rotation: {ex}"
            )
