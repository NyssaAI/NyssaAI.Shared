# Responsibility: Configuration options for out-of-band secret capture.
# Public interface:
# `-- CaptureSecretOptions
#     +-- allowed_providers
#     +-- confirm
#     +-- input_provider
#     +-- mode
#     +-- preferred_provider
#     +-- prompt_message
#     `-- timeout_seconds
"""Options configuring out-of-band secret capture."""

from __future__ import annotations

import dataclasses
from typing import TYPE_CHECKING

from nyssa_ai_configuration.secret_capture_mode import SecretCaptureMode

if TYPE_CHECKING:
    from nyssa_ai_configuration.secret_input.secret_input_provider import SecretInputProvider


@dataclasses.dataclass
class CaptureSecretOptions:
    """Options configuring out-of-band secret capture."""

    prompt_message: str | None = None
    preferred_provider: str | None = None
    allowed_providers: list[str] | None = None
    confirm: bool = True
    mode: SecretCaptureMode = SecretCaptureMode.AUTO
    timeout_seconds: float = 120.0
    input_provider: SecretInputProvider | None = None
