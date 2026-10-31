# Responsibility: Acquire and release persistent cross-process kernel file locks without unlinking lock files.
# Public interface:
# `-- FileLock
#     +-- acquire()
#     +-- new(target_file_path, timeout_ms, logger, clock)
#     `-- release()
"""Persistent cross-process and in-process file lock manager."""

from collections.abc import Callable
import os
from pathlib import Path
import sys
import threading
import time

if sys.platform == "win32":
    import msvcrt
else:
    import fcntl

from nyssa_ai_configuration.logging.configuration_logger import ConfigurationLogger
from nyssa_ai_configuration.results.result import Result, fail, ok

_IN_PROCESS_LOCKS_GUARD = threading.Lock()
_IN_PROCESS_LOCKS: dict[str, threading.Lock] = {}


def _get_in_process_lock(lock_path: Path) -> threading.Lock:
    key = os.path.normcase(str(lock_path))
    with _IN_PROCESS_LOCKS_GUARD:
        lock = _IN_PROCESS_LOCKS.get(key)
        if lock is None:
            lock = threading.Lock()
            _IN_PROCESS_LOCKS[key] = lock
        return lock


class FileLock:
    """Coordinates in-process and cross-process concurrency using a persistent .lock file."""

    def __init__(
        self,
        target_file_path: str | Path,
        timeout_ms: int = 10000,
        logger: ConfigurationLogger | None = None,
        clock: Callable[[], float] = time.monotonic,
    ) -> None:
        """Initialize lock for target_file_path with timeout_ms, optional logger, and monotonic clock."""
        self.target_path = Path(target_file_path).resolve()
        self.lock_path = self.target_path.with_name(f"{self.target_path.name}.lock")
        self.timeout_ms = timeout_ms
        self._logger = logger
        self._clock = clock
        self._fd: int | None = None
        self._thread_lock = _get_in_process_lock(self.lock_path)
        self._thread_lock_acquired = False

    def acquire(self) -> Result[None]:
        """Acquire both the in-process gate and OS kernel file lock within timeout_ms."""
        start_time = self._clock()
        timeout_sec = max(0.0, self.timeout_ms / 1000.0)
        backoff_sec = 0.005

        try:
            self.lock_path.parent.mkdir(parents=True, exist_ok=True)
        except OSError as exc:
            return fail(f"Failed to prepare lock directory for '{self.lock_path}': {exc}", "Forbidden", exc)

        if not self._thread_lock.acquire(timeout=timeout_sec if timeout_sec > 0 else -1):
            elapsed_ms = (self._clock() - start_time) * 1000.0
            if self._logger is not None:
                self._logger.degraded("file_lock.acquire", "lock.contention", elapsed_ms, "filesystem", 1)
                self._logger.completed("file_lock.acquire", "failure", elapsed_ms, "lock.timeout", "filesystem", 1)
            return fail(
                f"Timed out acquiring in-process lock on '{self.lock_path}' after {self.timeout_ms}ms.",
                "Unavailable",
            )
        self._thread_lock_acquired = True
        attempts = 0

        while True:
            attempts += 1
            fd: int | None = None
            try:
                fd = os.open(self.lock_path, os.O_CREAT | os.O_RDWR, 0o600)
                if sys.platform == "win32":
                    os.lseek(fd, 0, os.SEEK_SET)
                    msvcrt.locking(fd, msvcrt.LK_NBLCK, 1)
                else:
                    fcntl.flock(fd, fcntl.LOCK_EX | fcntl.LOCK_NB)
                self._fd = fd
                elapsed_ms = (self._clock() - start_time) * 1000.0
                if attempts > 1 and self._logger is not None:
                    self._logger.degraded("file_lock.acquire", "lock.retries", elapsed_ms, "filesystem", attempts)
                return ok(None)
            except (BlockingIOError, PermissionError, OSError) as exc:
                if fd is not None:
                    try:
                        os.close(fd)
                    except OSError:
                        pass
                elapsed = self._clock() - start_time
                if elapsed >= timeout_sec:
                    self._release_thread_lock()
                    if self._logger is not None:
                        self._logger.completed(
                            "file_lock.acquire",
                            "failure",
                            elapsed * 1000.0,
                            "lock.timeout",
                            "filesystem",
                            attempts,
                        )
                    return fail(
                        f"Timed out acquiring lock on '{self.lock_path}' after {self.timeout_ms}ms.",
                        "Unavailable",
                        exc,
                    )
                time.sleep(backoff_sec)
                backoff_sec = min(backoff_sec * 1.5, 0.05)

    def release(self) -> None:
        """Release the OS kernel lock and close the file descriptor without unlinking the .lock file."""
        try:
            if self._fd is not None:
                try:
                    if sys.platform == "win32":
                        try:
                            os.lseek(self._fd, 0, os.SEEK_SET)
                            msvcrt.locking(self._fd, msvcrt.LK_UNLCK, 1)
                        except OSError:
                            pass
                    else:
                        try:
                            fcntl.flock(self._fd, fcntl.LOCK_UN)
                        except OSError:
                            pass
                finally:
                    try:
                        os.close(self._fd)
                    except OSError:
                        pass
                    self._fd = None
        finally:
            self._release_thread_lock()

    def _release_thread_lock(self) -> None:
        if self._thread_lock_acquired:
            self._thread_lock_acquired = False
            self._thread_lock.release()
