# Responsibility: Fail-safe diagnostic reporting to standard error for internal logging faults.
# Public interface:
# `-- StderrDiagnostics
#     +-- error(subject, detail)
#     +-- format(level_tag, subject, detail)
#     +-- warning(subject, detail)
#     `-- write_line(line)
"""Diagnostics output to standard error."""

from __future__ import annotations

import os
import sys


class StderrDiagnostics:
    """Writes internal logging diagnostics directly to standard error without raising exceptions."""

    @staticmethod
    def error(subject: str, detail: str) -> None:
        """Write an error-level diagnostic about subject."""
        StderrDiagnostics._write("erro", subject, detail)

    @staticmethod
    def warning(subject: str, detail: str) -> None:
        """Write a warning-level diagnostic about subject."""
        StderrDiagnostics._write("warn", subject, detail)

    @staticmethod
    def format(level_tag: str, subject: str, detail: str) -> str:
        """Format one diagnostic line in the standard Nyssa stderr format."""
        return f"{level_tag}: {subject}{os.linesep}      {detail}"

    @staticmethod
    def write_line(line: str) -> None:
        """Write a formatted diagnostic line to sys.stderr, swallowing any stream exceptions."""
        try:
            sys.stderr.write(line + os.linesep)
            sys.stderr.flush()
        except BaseException:
            # Swallow all exceptions: broken stderr cannot be reported elsewhere.
            pass

    @staticmethod
    def _write(level_tag: str, subject: str, detail: str) -> None:
        StderrDiagnostics.write_line(StderrDiagnostics.format(level_tag, subject, detail))
