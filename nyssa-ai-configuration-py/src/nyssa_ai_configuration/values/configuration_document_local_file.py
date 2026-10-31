# Responsibility: Read, write, and atomically modify <namespace>.json documents under persistent kernel file locks.
# Public interface:
# `-- ConfigurationDocumentLocalFile
#     +-- modify(mutator)
#     +-- new(configuration_directory, namespace, lock_timeout_ms, write_interceptor, logger, clock)
#     +-- read()
#     `-- write(entries)
"""Local file-backed configuration namespace document."""

from collections.abc import Callable, Mapping
from pathlib import Path
import time

from nyssa_ai_configuration.file_security.directory_safety import DirectorySafety
from nyssa_ai_configuration.file_security.file_lock import FileLock
from nyssa_ai_configuration.file_security.protected_file_system import ProtectedFileSystem
from nyssa_ai_configuration.logging.configuration_logger import ConfigurationLogger
from nyssa_ai_configuration.results.result import Result, fail, ok
from nyssa_ai_configuration.values.configuration_value_codec import (
    ConfigurationValueCodec,
)
from nyssa_ai_configuration.values.types import ConfigurationValue


class ConfigurationDocumentLocalFile:
    """Manages a single <namespace>.json file with cross-process locking and atomic writes."""

    def __init__(
        self,
        configuration_directory: str | Path,
        namespace: str,
        lock_timeout_ms: int = 10000,
        write_interceptor: Callable[[str], Result[None]] | None = None,
        logger: ConfigurationLogger | None = None,
        clock: Callable[[], float] = time.monotonic,
    ) -> None:
        """Initialize for a specific namespace inside configuration_directory."""
        self.namespace = namespace
        self._dir = Path(str(configuration_directory)).resolve()
        self.file_path = (self._dir / f"{namespace}.json").resolve()
        self._lock_timeout_ms = lock_timeout_ms
        self._write_interceptor = write_interceptor
        self._logger = logger
        self._clock = clock

    def read(self) -> Result[dict[str, ConfigurationValue]]:
        """Read all entries from <namespace>.json under lock, returning an empty dict if the file does not exist."""
        valid = DirectorySafety.validate_segment(self.namespace, "namespace")
        if not valid.ok:
            return valid
        dir_safe = DirectorySafety.verify(self._dir)
        if not dir_safe.ok:
            return dir_safe
        if not self.file_path.exists():
            return ok({})

        lock = FileLock(self.file_path, timeout_ms=self._lock_timeout_ms, logger=self._logger, clock=self._clock)
        acq = lock.acquire()
        if not acq.ok:
            return acq
        try:
            return self._read_unlocked()
        finally:
            lock.release()

    def write(self, entries: Mapping[str, ConfigurationValue]) -> Result[None]:
        """Write entries atomically to <namespace>.json under lock."""
        valid = DirectorySafety.validate_segment(self.namespace, "namespace")
        if not valid.ok:
            return valid
        dir_safe = DirectorySafety.verify(self._dir)
        if not dir_safe.ok:
            return dir_safe

        lock = FileLock(self.file_path, timeout_ms=self._lock_timeout_ms, logger=self._logger, clock=self._clock)
        acq = lock.acquire()
        if not acq.ok:
            return acq
        try:
            return self._write_unlocked(entries)
        finally:
            lock.release()

    def modify(
        self,
        mutator: Callable[[dict[str, ConfigurationValue]], Result[bool]],
    ) -> Result[dict[str, ConfigurationValue]]:
        """Acquire the namespace lock, read current entries, invoke mutator, and publish when mutator returns Ok(True)."""
        valid = DirectorySafety.validate_segment(self.namespace, "namespace")
        if not valid.ok:
            return valid
        dir_safe = DirectorySafety.verify(self._dir)
        if not dir_safe.ok:
            return dir_safe
        dir_create = ProtectedFileSystem.create_directory(self._dir)
        if not dir_create.ok:
            return dir_create

        lock = FileLock(self.file_path, timeout_ms=self._lock_timeout_ms, logger=self._logger, clock=self._clock)
        acq = lock.acquire()
        if not acq.ok:
            return acq
        try:
            loaded = self._read_unlocked()
            if not loaded.ok:
                return loaded
            entries = dict(loaded.value)
            mut_res = mutator(entries)
            if not mut_res.ok:
                return mut_res
            if mut_res.value:
                written = self._write_unlocked(entries)
                if not written.ok:
                    return written
            return ok(entries)
        finally:
            lock.release()

    def _read_unlocked(self) -> Result[dict[str, ConfigurationValue]]:
        if not self.file_path.exists():
            return ok({})
        file_safe = DirectorySafety.verify_file(self.file_path)
        if not file_safe.ok:
            return file_safe
        try:
            raw = self.file_path.read_text(encoding="utf-8")
        except Exception as exc:
            return fail(f"Failed to read '{self.file_path}': {exc}", "Unavailable", exc)
        return ConfigurationValueCodec.decode_document(raw)

    def _write_unlocked(self, entries: Mapping[str, ConfigurationValue]) -> Result[None]:
        if self._write_interceptor is not None:
            intercepted = self._write_interceptor(self.namespace)
            if not intercepted.ok:
                return intercepted
        encoded = ConfigurationValueCodec.encode_document(entries)
        if not encoded.ok:
            return encoded
        write_res = ProtectedFileSystem.create_file(self.file_path, encoded.value)
        if not write_res.ok:
            return write_res
        return ok(None)
