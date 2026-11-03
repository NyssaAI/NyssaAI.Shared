# Responsibility: Enumeration of execution modes for out-of-band secret capture.
# Public interface:
# `-- SecretCaptureMode
#     +-- AUTO
#     +-- CLI
#     `-- WEB
"""Execution modes for out-of-band secret capture."""

from __future__ import annotations

from enum import Enum


class SecretCaptureMode(Enum):
    """Execution mode for out-of-band secret capture."""

    AUTO = "auto"
    CLI = "cli"
    WEB = "web"
