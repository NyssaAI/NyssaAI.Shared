# Responsibility: Define the SecretInputProvider protocol for out-of-band secret input prompts.
# Public interface:
# `-- SecretInputProvider
#     `-- capture(request)
"""Secret input provider interface."""

from __future__ import annotations

from typing import Protocol

from nyssa_ai_configuration.secret_input.captured_secret import CapturedSecret
from nyssa_ai_configuration.secret_input.secret_prompt_request import SecretPromptRequest
from nyssa_ai_result import Result


class SecretInputProvider(Protocol):
    """Protocol for capturing secrets out-of-band directly from the user."""

    def capture(self, request: SecretPromptRequest) -> Result[CapturedSecret]:
        """Execute the prompt flow and return the captured secret buffer."""
        ...
