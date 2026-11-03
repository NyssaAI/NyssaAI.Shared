# Responsibility: Define the ConfigurationSecretWriter protocol for out-of-band secret capture.
# Public interface:
# `-- ConfigurationSecretWriter
#     `-- capture_secret(namespace, key, options)
"""Secret capture protocol interface."""

from __future__ import annotations

from typing import Protocol

from nyssa_ai_configuration.capture_secret_options import CaptureSecretOptions
from nyssa_ai_configuration.secret_receipt import SecretReceipt
from nyssa_ai_result import Result


class ConfigurationSecretWriter(Protocol):
    """Interface for capturing secrets out-of-band directly from the user."""

    def capture_secret(
        self,
        namespace: str,
        key: str,
        options: CaptureSecretOptions | None = None,
    ) -> Result[SecretReceipt]:
        """Prompt user out-of-band for a secret, encrypt or vault it, and return a sanitized receipt."""
        ...
