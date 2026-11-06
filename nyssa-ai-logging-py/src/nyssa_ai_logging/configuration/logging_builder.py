# Responsibility: Configure stdlib logging handlers, per-package levels, and OpenTelemetry instrumentation.
# Public interface:
# +-- LoggingBuilder
# |   +-- add_handler(handler)
# |   +-- add_nyssa_json_file(file_path, minimum_level)
# |   +-- dispose()
# |   +-- log_file_paths
# |   +-- new(minimum_level, clock, logger_name)
# |   +-- otel_records
# |   +-- set_minimum_level(level)
# |   +-- set_package_level(package_name, level)
# |   +-- start_span(name, attributes, operation)
# |   +-- with_console(minimum_level)
# |   +-- with_otel(enabled, minimum_level)
# |   +-- with_rotating_json_log(file_path, minimum_level, app_name, log_dir)
# |   `-- with_stderr(minimum_level)
# `-- resolve_app_name(app_name)
"""Fluent configurator attaching Nyssa handlers and OpenTelemetry to Python's stdlib logging."""

from __future__ import annotations

from collections.abc import Mapping
from contextlib import AbstractContextManager
import logging
import os
from pathlib import Path
import re
import sys
from types import TracebackType
from typing import final

from nyssa_ai_logging.console_logging.create_console_handler import create_console_handler
from nyssa_ai_logging.console_logging.create_stderr_handler import create_stderr_handler
from nyssa_ai_logging.file_logging.clock import Clock
from nyssa_ai_logging.file_logging.create_json_file_handler import create_json_file_handler
from nyssa_ai_logging.file_logging.log_record import LogRecord
from nyssa_ai_logging.file_logging.system_clock import SystemClock
from nyssa_ai_logging.log_level import LogLevel, parse_log_level, to_python_log_level
from nyssa_ai_logging.telemetry.create_otel_handler import create_otel_handler
from nyssa_ai_logging.telemetry.span_scope import SpanScope
from nyssa_ai_logging.telemetry.start_span import set_otel_enabled, start_span

_ACTIVE_BUILDERS: dict[str, LoggingBuilder] = {}


def resolve_app_name(app_name: str | None = None) -> str:
    """Resolve a safe application name for default rotating log file naming."""
    candidate = (app_name or "").strip()
    if not candidate and sys.argv and sys.argv[0]:
        stem = Path(sys.argv[0]).stem.strip()
        if stem not in {"", "-c", "-m", "__main__", "pytest", "__init__"}:
            candidate = stem
    sanitized = re.sub(r"[^A-Za-z0-9._-]+", "-", candidate).strip("-._")
    if sanitized.endswith(".json"):
        sanitized = sanitized[:-5].rstrip("-._")
    if sanitized.endswith("-log"):
        sanitized = sanitized[:-4].rstrip("-._")
    return sanitized or "application"


@final
class LoggingBuilder:
    """Configures Python stdlib logging handlers, package thresholds, and OpenTelemetry."""

    def __init__(
        self,
        minimum_level: LogLevel | int = "Information",
        clock: Clock | None = None,
        logger_name: str | None = None,
    ) -> None:
        """Initialize stdlib logging configuration on root (or named) logger."""
        self._logger_key = logger_name or ""
        previous = _ACTIVE_BUILDERS.get(self._logger_key)
        if previous is not None and previous is not self:
            previous.dispose()

        self._target_logger = logging.getLogger(logger_name)
        self._previous_level = self._target_logger.level
        self._minimum_level: LogLevel = parse_log_level(minimum_level)
        self._clock: Clock = clock if clock is not None else SystemClock()
        self._handlers: list[logging.Handler] = []
        self._package_previous_levels: dict[str, int] = {}
        self._log_file_paths: list[str] = []
        self._otel_records: list[LogRecord] = []
        self._has_stderr = False
        self._has_console = False
        self._has_otel = False
        self._disposed = False

        self._target_logger.setLevel(to_python_log_level(self._minimum_level))
        _ACTIVE_BUILDERS[self._logger_key] = self

    @property
    def log_file_paths(self) -> tuple[str, ...]:
        """Return the file paths of active rotating JSON file destinations."""
        return tuple(self._log_file_paths)

    @property
    def otel_records(self) -> tuple[LogRecord, ...]:
        """Return records captured by the OpenTelemetry logging handler when enabled."""
        return tuple(self._otel_records)

    def set_minimum_level(self, level: LogLevel | int) -> LoggingBuilder:
        """Set the minimum level on the target stdlib logger."""
        self._minimum_level = parse_log_level(level)
        self._target_logger.setLevel(to_python_log_level(level))
        return self

    def set_package_level(self, package_name: str, level: LogLevel | int) -> LoggingBuilder:
        """Configure the log level for a specific Python package or module ('a.b.c')."""
        if not package_name or not package_name.strip():
            return self
        pkg_logger = logging.getLogger(package_name)
        if package_name not in self._package_previous_levels:
            self._package_previous_levels[package_name] = pkg_logger.level
        pkg_logger.setLevel(to_python_log_level(level))
        return self

    def with_stderr(self, minimum_level: LogLevel | int | None = None) -> LoggingBuilder:
        """Attach a standard error logging.Handler if not already registered."""
        if not self._has_stderr:
            self._has_stderr = True
            handler = create_stderr_handler(minimum_level)
            self.add_handler(handler)
        return self

    def with_console(self, minimum_level: LogLevel | int | None = None) -> LoggingBuilder:
        """Attach a standard output logging.Handler if not already registered."""
        if not self._has_console:
            self._has_console = True
            handler = create_console_handler(minimum_level)
            self.add_handler(handler)
        return self

    def with_rotating_json_log(
        self,
        file_path: str | None = None,
        minimum_level: LogLevel | int | None = None,
        *,
        app_name: str | None = None,
        log_dir: str = "./logs",
    ) -> LoggingBuilder:
        """Attach a rotating JSON file named yyyy.mm.dd-{appname}-log.json (or dated file_path)."""
        date_str = self._clock.now().strftime("%Y.%m.%d")
        if file_path is not None and file_path.strip():
            directory = os.path.dirname(file_path)
            effective_dir = (
                os.path.abspath(directory) if directory and directory != "." else os.getcwd()
            )
            base_name = os.path.basename(file_path)
            dated_name = (
                base_name if base_name.startswith(f"{date_str}-") else f"{date_str}-{base_name}"
            )
            destination = os.path.join(effective_dir, dated_name)
        else:
            effective_dir = os.path.abspath(log_dir.strip() if log_dir and log_dir.strip() else "./logs")
            resolved_app = resolve_app_name(app_name)
            destination = os.path.join(effective_dir, f"{date_str}-{resolved_app}-log.json")

        return self.add_nyssa_json_file(destination, minimum_level)

    def add_nyssa_json_file(
        self,
        file_path: str,
        minimum_level: LogLevel | int | None = None,
    ) -> LoggingBuilder:
        """Attach a structured rotating JSON file logging.Handler at the exact supplied path."""
        handler = create_json_file_handler(
            file_path=file_path,
            minimum_level=minimum_level,
            clock=self._clock,
        )
        if handler is not None:
            self._log_file_paths.append(file_path)
            self.add_handler(handler)
        return self

    def with_otel(
        self,
        enabled: bool = True,
        minimum_level: LogLevel | int | None = None,
    ) -> LoggingBuilder:
        """Enable or disable OpenTelemetry log bridging and programmatic span support."""
        set_otel_enabled(enabled)
        if enabled and not self._has_otel:
            self._has_otel = True
            handler = create_otel_handler(
                minimum_level=minimum_level,
                clock=self._clock,
                record_sink=self._otel_records.append,
            )
            self.add_handler(handler)
        return self

    def add_handler(self, handler: logging.Handler) -> LoggingBuilder:
        """Attach a standard Python logging.Handler to the configured logger."""
        self._handlers.append(handler)
        self._target_logger.addHandler(handler)
        return self

    def start_span(
        self,
        name: str,
        attributes: Mapping[str, object] | None = None,
        operation: str = "",
    ) -> AbstractContextManager[SpanScope]:
        """Start a correlated OpenTelemetry span around a block of work."""
        return start_span(name=name, attributes=attributes, operation=operation)

    def dispose(self) -> None:
        """Detach and close all handlers owned by this builder and restore logger levels."""
        if self._disposed:
            return
        self._disposed = True

        for handler in self._handlers:
            self._target_logger.removeHandler(handler)
            try:
                handler.flush()
                handler.close()
            except Exception:
                pass
        self._handlers.clear()

        for pkg_name, prev_level in self._package_previous_levels.items():
            logging.getLogger(pkg_name).setLevel(prev_level)
        self._package_previous_levels.clear()

        self._target_logger.setLevel(self._previous_level)
        if self._has_otel:
            set_otel_enabled(False)
            self._has_otel = False

        if _ACTIVE_BUILDERS.get(self._logger_key) is self:
            del _ACTIVE_BUILDERS[self._logger_key]

    def __enter__(self) -> LoggingBuilder:
        """Enter context manager."""
        return self

    def __exit__(
        self,
        exc_type: type[BaseException] | None,
        exc_val: BaseException | None,
        exc_tb: TracebackType | None,
    ) -> None:
        """Exit context manager and dispose handlers."""
        self.dispose()
