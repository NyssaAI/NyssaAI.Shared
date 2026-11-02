# Responsibility: External cloud key vault adapter backed by the Bitwarden Secrets Manager CLI (bws).
# Public interface:
# `-- CloudKeyVaultBitwarden
#     +-- ACCESS_TOKEN_ENV
#     +-- DEFAULT_BINARY
#     +-- ORGANIZATION_ID_ENV
#     +-- SERVER_URL_ENV
#     +-- name
#     +-- new(options, token_source, command_runner)
#     +-- read(pointer)
#     +-- recovery
#     +-- remove(pointer)
#     `-- write(namespace, key, value)
"""Bitwarden Secrets Manager (bws) implementation of CloudKeyVault."""

from __future__ import annotations

import json
import os
import subprocess
from collections.abc import Callable, Mapping, Sequence

from nyssa_ai_result import Result, fail, ok

from nyssa_ai_configuration.cloud_key_vaults.access_token_source import AccessTokenSource
from nyssa_ai_configuration.cloud_key_vaults.cloud_key_vault_recovery import (
    CloudKeyVaultRecovery,
)
from nyssa_ai_configuration.cloud_key_vaults.types import ConfigurationPointer
from nyssa_ai_configuration.manifest.types import VaultOptions

type _BwsCommandRunner = Callable[[str, Sequence[str], Mapping[str, str]], Result[str]]


class CloudKeyVaultBitwarden:
    """CloudKeyVault adapter backed by the Bitwarden Secrets Manager CLI (bws)."""

    DEFAULT_BINARY: str = "bws"
    ACCESS_TOKEN_ENV: str = "BWS_ACCESS_TOKEN"
    ORGANIZATION_ID_ENV: str = "BWS_ORGANIZATION_ID"
    SERVER_URL_ENV: str = "BWS_SERVER_URL"

    def __init__(
        self,
        options: VaultOptions,
        token_source: AccessTokenSource | None = None,
        command_runner: _BwsCommandRunner | None = None,
    ) -> None:
        """Initialize the Bitwarden Secrets Manager vault adapter from VaultOptions."""
        self._name = options.name
        self._project_id = self._read_non_blank(options.settings.get("projectId"))
        self._organization_id = self._read_non_blank(options.settings.get("organizationId"))
        self._server_url = (
            self._read_non_blank(options.endpoint)
            or self._read_non_blank(options.settings.get("serverUrl"))
            or self._read_non_blank(options.settings.get("apiUrl"))
        )
        self._binary_path = (
            self._read_non_blank(options.settings.get("cliPath"))
            or self._read_non_blank(options.settings.get("bwsCliPath"))
            or self.DEFAULT_BINARY
        )
        self._static_access_token = self._read_non_blank(
            options.secrets.get("accessToken")
        ) or self._read_non_blank(options.secrets.get("machineAccessToken"))
        self._token_source = token_source if self._read_non_blank(options.credential) else None
        self._runner: _BwsCommandRunner = command_runner or self._default_command_runner

    @property
    def name(self) -> str:
        """Return the configured registration name of this vault instance."""
        return self._name

    @property
    def recovery(self) -> CloudKeyVaultRecovery | None:
        """Return None because Bitwarden Secrets Manager CLI uses direct CRUD without two-phase recovery."""
        return None

    def read(self, pointer: ConfigurationPointer) -> Result[str]:
        """Read the cleartext secret referenced by pointer from Bitwarden Secrets Manager."""
        if not pointer.value or not pointer.value.strip():
            return fail("Vault pointer value must not be empty.", "Validation")

        env_res = self._resolve_environment()
        if not env_res.ok:
            return env_res

        list_res = self._list_secrets(env_res.value)
        if not list_res.ok:
            return list_res

        match_id = self._find_matching_id(list_res.value, pointer.value)
        if match_id is None:
            return fail(
                f"Secret '{pointer.value}' was not found in Bitwarden Secrets Manager vault '{self._name}'.",
                "NotFound",
            )

        out_res = self._runner(
            self._binary_path,
            ["secret", "get", match_id, "--output", "json"],
            env_res.value,
        )
        if not out_res.ok:
            return out_res

        try:
            parsed: object = json.loads(out_res.value)
        except (json.JSONDecodeError, ValueError, TypeError) as exc:
            return fail(
                f"Vault '{self._name}' returned invalid JSON from bws secret get: {exc}",
                "Unavailable",
                exc,
            )

        if not isinstance(parsed, dict) or not isinstance(parsed.get("value"), str):
            return fail(
                f"Vault '{self._name}' returned a secret payload without a string value.",
                "Unavailable",
            )
        return ok(parsed["value"])

    def write(self, namespace: str, key: str, value: str) -> Result[ConfigurationPointer]:
        """Create or edit the secret for (namespace, key) in Bitwarden Secrets Manager."""
        if not namespace.strip() or not key.strip():
            return fail("Namespace and key must not be empty.", "Validation")

        env_res = self._resolve_environment()
        if not env_res.ok:
            return env_res

        secret_name = f"{namespace}/{key}"
        list_res = self._list_secrets(env_res.value)
        if not list_res.ok:
            return list_res

        existing_id = self._find_matching_id(list_res.value, secret_name)
        if existing_id is not None:
            args = ["secret", "edit", existing_id, "--value", value, "--output", "json"]
        else:
            if not self._project_id:
                return fail(
                    f"Vault '{self._name}' requires settings['projectId'] to create new secrets in Bitwarden Secrets Manager.",
                    "Validation",
                )
            args = ["secret", "create", secret_name, value, self._project_id, "--output", "json"]

        write_res = self._runner(self._binary_path, args, env_res.value)
        if not write_res.ok:
            return write_res

        return ok(ConfigurationPointer(provider=self._name, value=secret_name))

    def remove(self, pointer: ConfigurationPointer) -> Result[bool]:
        """Delete pointer from Bitwarden Secrets Manager and return Ok(False) indicating purge."""
        if not pointer.value or not pointer.value.strip():
            return fail("Vault pointer value must not be empty.", "Validation")

        env_res = self._resolve_environment()
        if not env_res.ok:
            return env_res

        list_res = self._list_secrets(env_res.value)
        if not list_res.ok:
            return list_res

        existing_id = self._find_matching_id(list_res.value, pointer.value)
        if existing_id is None:
            return ok(False)

        del_res = self._runner(
            self._binary_path,
            ["secret", "delete", existing_id],
            env_res.value,
        )
        if not del_res.ok:
            return del_res
        return ok(False)

    def _resolve_environment(self) -> Result[dict[str, str]]:
        token = self._static_access_token
        if self._token_source is not None:
            tok_res = self._token_source.get_access_token()
            if not tok_res.ok:
                return tok_res
            token = tok_res.value

        if not token or not token.strip():
            return fail(
                f"Vault '{self._name}' requires either key_vault.credential or secrets['accessToken'].",
                "Validation",
            )

        env: dict[str, str] = {self.ACCESS_TOKEN_ENV: token}
        if self._organization_id:
            env[self.ORGANIZATION_ID_ENV] = self._organization_id
        if self._server_url:
            env[self.SERVER_URL_ENV] = self._server_url
        return ok(env)

    def _list_secrets(self, env: Mapping[str, str]) -> Result[list[dict[str, object]]]:
        args = (
            ["secret", "list", self._project_id, "--output", "json"]
            if self._project_id
            else ["secret", "list", "--output", "json"]
        )
        res = self._runner(self._binary_path, args, env)
        if not res.ok:
            msg = res.error.message if res.error else ""
            if "404" in msg and "not found" in msg.lower():
                return ok([])
            return res

        try:
            parsed: object = json.loads(res.value)
        except (json.JSONDecodeError, ValueError, TypeError) as exc:
            return fail(
                f"Vault '{self._name}' returned invalid JSON for secret list: {exc}",
                "Unavailable",
                exc,
            )

        if not isinstance(parsed, list):
            return fail(
                f"Vault '{self._name}' returned non-array JSON for secret list.",
                "Unavailable",
            )

        items: list[dict[str, object]] = [
            item for item in parsed if isinstance(item, dict)
        ]
        return ok(items)

    @staticmethod
    def _find_matching_id(items: Sequence[Mapping[str, object]], pointer_value: str) -> str | None:
        dot_name = pointer_value.replace("/", ".")
        for item in items:
            item_id = item.get("id")
            if not isinstance(item_id, str) or not item_id.strip():
                continue
            raw_key = item.get("key") if isinstance(item.get("key"), str) else item.get("slug")
            resolved_key = raw_key if isinstance(raw_key, str) else ""
            if resolved_key in (pointer_value, dot_name) or item_id == pointer_value:
                return item_id
        return None

    @staticmethod
    def _read_non_blank(value: str | None) -> str | None:
        if value is None:
            return None
        trimmed = value.strip()
        return trimmed if trimmed else None

    @staticmethod
    def _default_command_runner(
        binary_path: str,
        args: Sequence[str],
        env: Mapping[str, str],
    ) -> Result[str]:
        merged_env = dict(os.environ)
        merged_env.update(env)
        try:
            completed = subprocess.run(
                [binary_path, *args],
                env=merged_env,
                capture_output=True,
                text=True,
                encoding="utf-8",
                check=False,
            )
        except OSError as exc:
            return fail(
                f"Failed to start Bitwarden CLI '{binary_path}': {exc}",
                "Unavailable",
                exc,
            )

        if completed.returncode != 0:
            detail = completed.stderr.strip() or completed.stdout.strip() or f"exit code {completed.returncode}"
            lower = detail.lower()
            category = (
                "Forbidden"
                if "401" in detail
                or "403" in detail
                or "unauthorized" in lower
                or "unauthenticated" in lower
                else "Unavailable"
            )
            return fail(f"Bitwarden CLI command failed: {detail}", category)

        return ok(completed.stdout)
