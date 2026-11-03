# Responsibility: Adaptively route between CLI masked input and Web loopback based on terminal interactivity.
# Public interface:
# `-- AdaptiveSecretInputProvider
#     +-- capture(request)
#     `-- new(mode, browser_launcher, stream_in, stream_out)
"""Adaptive secret input provider."""

from __future__ import annotations

import sys
from typing import TextIO

from nyssa_ai_configuration.oauth.browser_launcher import BrowserLauncher
from nyssa_ai_configuration.secret_capture_mode import SecretCaptureMode
from nyssa_ai_configuration.secret_input.cli_secret_input_provider import (
    CliSecretInputProvider,
)
from nyssa_ai_configuration.secret_input.captured_secret import CapturedSecret
from nyssa_ai_configuration.secret_input.loopback_secret_input_provider import (
    LoopbackSecretInputProvider,
)
from nyssa_ai_configuration.secret_input.secret_prompt_request import SecretPromptRequest
from nyssa_ai_result import Result


class AdaptiveSecretInputProvider:
    """Detects whether stdin is an interactive TTY and delegates to CLI or Web loopback."""

    def __init__(
        self,
        mode: SecretCaptureMode = SecretCaptureMode.AUTO,
        browser_launcher: BrowserLauncher | None = None,
        stream_in: TextIO | None = None,
        stream_out: TextIO | None = None,
    ) -> None:
        self._mode = mode
        self._browser_launcher = browser_launcher
        self._stream_in = stream_in
        self._stream_out = stream_out

    def capture(self, request: SecretPromptRequest) -> Result[CapturedSecret]:
        """Execute capture according to resolved mode."""
        resolved = self._mode
        if resolved == SecretCaptureMode.AUTO:
            is_tty = hasattr(sys.stdin, "isatty") and sys.stdin.isatty()
            resolved = SecretCaptureMode.CLI if is_tty else SecretCaptureMode.WEB

        if resolved == SecretCaptureMode.CLI:
            return CliSecretInputProvider(self._stream_in, self._stream_out).capture(request)
        else:
            return LoopbackSecretInputProvider(self._browser_launcher, self._stream_out).capture(request)
