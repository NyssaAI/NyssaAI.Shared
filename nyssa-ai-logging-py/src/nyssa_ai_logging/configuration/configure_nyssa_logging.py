# Responsibility: Configure Python stdlib logging with safe Nyssa defaults in a single call.
# Public interface:
# `-- configure_nyssa_logging(file_path, minimum_level, app_name, log_dir, enable_otel, enable_file, package_levels, clock)
"""Single-call setup configuring Python's stdlib logging with stderr, rotating JSON file, and optional OTel."""

from __future__ import annotations

from collections.abc import Mapping

from nyssa_ai_logging.configuration.logging_builder import LoggingBuilder
from nyssa_ai_logging.file_logging.clock import Clock
from nyssa_ai_logging.log_level import LogLevel


def configure_nyssa_logging(
    file_path: str | None = None,
    minimum_level: LogLevel | int = "Information",
    *,
    app_name: str | None = None,
    log_dir: str = "./logs",
    enable_otel: bool = False,
    enable_file: bool = True,
    package_levels: Mapping[str, LogLevel | int] | None = None,
    clock: Clock | None = None,
) -> LoggingBuilder:
    """Configure Python's root logger with stderr, rotating JSON file in ./logs, and optional OTel."""
    builder = LoggingBuilder(minimum_level=minimum_level, clock=clock)
    builder.with_stderr()

    if enable_file:
        if file_path is not None:
            builder.add_nyssa_json_file(file_path)
        else:
            builder.with_rotating_json_log(app_name=app_name, log_dir=log_dir)

    if enable_otel:
        builder.with_otel(True)

    if package_levels is not None:
        for pkg_name, pkg_level in package_levels.items():
            builder.set_package_level(pkg_name, pkg_level)

    return builder
