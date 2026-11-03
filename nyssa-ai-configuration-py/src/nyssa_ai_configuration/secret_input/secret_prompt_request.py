# Responsibility: Request contract passed to a SecretInputProvider.
# Public interface:
# `-- SecretPromptRequest
#     +-- available_providers
#     +-- confirm
#     +-- key
#     +-- namespace
#     +-- preferred_provider
#     +-- prompt_message
#     `-- timeout_seconds
"""Secret prompt request data model."""

from __future__ import annotations

import dataclasses


@dataclasses.dataclass(frozen=True)
class SecretPromptRequest:
    """Request contract passed to a SecretInputProvider."""

    namespace: str
    key: str
    prompt_message: str
    available_providers: list[str]
    preferred_provider: str
    confirm: bool
    timeout_seconds: float
