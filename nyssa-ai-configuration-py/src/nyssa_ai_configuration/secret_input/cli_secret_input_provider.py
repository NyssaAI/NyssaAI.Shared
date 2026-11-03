# Responsibility: Capture secrets from terminal stdin using masked password prompts.
# Public interface:
# `-- CliSecretInputProvider
#     +-- capture(request)
#     `-- new(stream_in, stream_out)
"""CLI terminal masked secret input provider."""

from __future__ import annotations

import getpass
import sys
from typing import TextIO

from nyssa_ai_configuration.secret_input.captured_secret import CapturedSecret
from nyssa_ai_configuration.secret_input.secret_prompt_request import SecretPromptRequest
from nyssa_ai_result import Result, fail, ok


def _format_provider_label(name: str) -> str:
    lower = name.lower()
    if lower == "aeslocal":
        return "Encrypted Locally (AES-256-GCM)"
    if lower in ("literal", "default", "plaintext", ""):
        return "Free Text (Unencrypted Literal)"
    if lower in ("bws", "bitwarden", "bitwarden-secrets"):
        return "Key Vault (Bitwarden Secrets Manager)"
    if lower.startswith("azure"):
        return "Key Vault (Azure Key Vault)"
    if lower.startswith("hashi") or lower == "vault":
        return "Key Vault (HashiCorp Vault)"
    return f"Key Vault ({name})"


class CliSecretInputProvider:
    """Masked terminal input provider using getpass."""

    def __init__(self, stream_in: TextIO | None = None, stream_out: TextIO | None = None) -> None:
        self._stream_in = stream_in or sys.stdin
        self._stream_out = stream_out or sys.stdout

    def capture(self, request: SecretPromptRequest) -> Result[CapturedSecret]:
        """Prompt the user masked in the terminal."""
        try:
            self._stream_out.write("\n")
            title = request.prompt_message or f"Enter secret for {request.namespace}:{request.key}"
            self._stream_out.write(f"{title} ({request.namespace}:{request.key})\n")

            selected_provider = request.preferred_provider
            if len(request.available_providers) == 1:
                label = _format_provider_label(request.available_providers[0])
                self._stream_out.write(f"Storage destination: {label} [{request.available_providers[0]}] (Fixed by application)\n")
            elif len(request.available_providers) > 1:
                self._stream_out.write("Select storage destination:\n")
                default_idx = 1
                for idx, prov in enumerate(request.available_providers, start=1):
                    is_def = prov.lower() == request.preferred_provider.lower()
                    if is_def:
                        default_idx = idx
                    def_str = " (Default)" if is_def else ""
                    label = _format_provider_label(prov)
                    self._stream_out.write(f"  [{idx}] {label} [{prov}]{def_str}\n")

                self._stream_out.write(f"Destination [{default_idx}]: ")
                self._stream_out.flush()
                choice = self._stream_in.readline().strip()
                if choice.isdigit():
                    num = int(choice)
                    if 1 <= num <= len(request.available_providers):
                        selected_provider = request.available_providers[num - 1]

            first_entry = getpass.getpass("Enter secret: ", stream=self._stream_out)

            if request.confirm:
                second_entry = getpass.getpass("Confirm secret: ", stream=self._stream_out)
                if first_entry != second_entry:
                    return fail("The entered secrets do not match.", "Validation")

            return ok(CapturedSecret(first_entry, selected_provider))
        except Exception as exc:
            return fail(f"Failed to capture secret from CLI: {exc}", "Unexpected", exc)
