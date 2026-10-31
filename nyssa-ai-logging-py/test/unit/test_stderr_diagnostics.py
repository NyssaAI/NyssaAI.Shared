"""Unit tests for StderrDiagnostics."""

from __future__ import annotations

import os

from nyssa_ai_logging.file_logging.stderr_diagnostics import StderrDiagnostics
from test.unit.helpers.stderr_capture import StderrCapture


def test_diagnostics_format_produces_canonical_format() -> None:
    formatted = StderrDiagnostics.format("erro", "application.log", "failed to open")
    assert formatted == f"erro: application.log{os.linesep}      failed to open"


def test_diagnostics_error_writes_to_stderr_without_throwing() -> None:
    with StderrCapture() as stderr:
        StderrDiagnostics.error("config.log", "access denied")
        assert "erro: config.log" in stderr.text
        assert "access denied" in stderr.text


def test_diagnostics_warning_writes_to_stderr_without_throwing() -> None:
    with StderrCapture() as stderr:
        StderrDiagnostics.warning("rotation", "slow I/O")
        assert "warn: rotation" in stderr.text
        assert "slow I/O" in stderr.text
