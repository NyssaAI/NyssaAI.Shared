# Responsibility: Fluent builder for configuring logging destinations and creating loggers.
# Public interface:
# `-- LoggingBuilder
#     +-- add_nyssa_json_file(file_path, minimum_level)
#     +-- add_provider(provider)
#     +-- create_logger(category_name)
#     +-- dispose()
#     +-- new(minimum_level, clock)
#     +-- set_minimum_level(level)
#     +-- with_console(minimum_level)
#     +-- with_rotating_json_log(file_path, minimum_level)
#     `-- with_stderr(minimum_level)
"""Fluent builder for registering destinations and resolving loggers."""

from __future__ import annotations

import os
from types import TracebackType

from nyssa_ai_logging.log_level import LogLevel
from nyssa_ai_logging.logger import Logger
from nyssa_ai_logging.logger_provider import LoggerProvider
from nyssa_ai_logging.file_logging.clock import Clock
from nyssa_ai_logging.file_logging.system_clock import SystemClock
from nyssa_ai_logging.file_logging.json_file_logger_provider import JsonFileLoggerProvider
from nyssa_ai_logging.console_logging.stderr_output_logger_provider import StderrOutputLoggerProvider
from nyssa_ai_logging.console_logging.console_output_logger_provider import ConsoleOutputLoggerProvider
from nyssa_ai_logging.configuration.composite_logger import CompositeLogger


class LoggingBuilder:
    """Configures logging destinations and creates category-scoped loggers."""

    def __init__(
        self,
        minimum_level: LogLevel = "Information",
        clock: Clock | None = None,
    ) -> None:
        """Initialize with minimum log level and optional clock."""
        self._minimum_level = minimum_level
        self._clock = clock if clock is not None else SystemClock()
        self._providers: list[LoggerProvider] = []
        self._has_stderr = False
        self._has_console = False
        self._disposed = False

    def set_minimum_level(self, level: LogLevel) -> LoggingBuilder:
        """Set the default minimum level for subsequent providers."""
        self._minimum_level = level
        return self

    def with_stderr(self, minimum_level: LogLevel | None = None) -> LoggingBuilder:
        """Add standard error output if not already present."""
        if not self._has_stderr:
            self._has_stderr = True
            eff_level = minimum_level if minimum_level is not None else self._minimum_level
            self._providers.append(StderrOutputLoggerProvider(eff_level))
        return self

    def with_console(self, minimum_level: LogLevel | None = None) -> LoggingBuilder:
        """Add standard output if not already present."""
        if not self._has_console:
            self._has_console = True
            eff_level = minimum_level if minimum_level is not None else self._minimum_level
            self._providers.append(ConsoleOutputLoggerProvider(eff_level))
        return self

    def with_rotating_json_log(
        self,
        file_path: str,
        minimum_level: LogLevel | None = None,
    ) -> LoggingBuilder:
        """Add a rotating JSON file whose name starts with its opening date in UTC."""
        directory = os.path.dirname(file_path)
        effective_dir = directory if directory and directory != "." else os.getcwd()
        date_str = self._clock.now().strftime("%Y.%m.%d")
        base_name = os.path.basename(file_path)
        destination = os.path.join(effective_dir, f"{date_str}-{base_name}")
        return self.add_nyssa_json_file(destination, minimum_level)

    def add_nyssa_json_file(
        self,
        file_path: str,
        minimum_level: LogLevel | None = None,
    ) -> LoggingBuilder:
        """Add a structured rotating JSON file destination at the exact supplied path."""
        eff_level = minimum_level if minimum_level is not None else self._minimum_level
        provider = JsonFileLoggerProvider.try_create(
            file_path, self._clock, eff_level
        )
        if provider is not None:
            self._providers.append(provider)
        return self

    def add_provider(self, provider: LoggerProvider) -> LoggingBuilder:
        """Add an arbitrary LoggerProvider."""
        self._providers.append(provider)
        return self

    def create_logger(self, category_name: str) -> Logger:
        """Resolve a composite Logger for category_name across all registered providers."""
        loggers = [p.create_logger(category_name) for p in self._providers]
        return CompositeLogger(loggers)

    def dispose(self) -> None:
        """Dispose all registered providers and flush pending writes."""
        if self._disposed:
            return
        self._disposed = True
        for provider in self._providers:
            provider.dispose()

    def __enter__(self) -> LoggingBuilder:
        """Enter context manager."""
        return self

    def __exit__(
        self,
        exc_type: type[BaseException] | None,
        exc_val: BaseException | None,
        exc_tb: TracebackType | None,
    ) -> None:
        """Exit context manager and dispose providers."""
        self.dispose()
