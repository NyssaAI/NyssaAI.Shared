# Responsibility: Read and validate config-manifest.json with JSONC stripping, private secret checks, and millisecond timeouts.
# Public interface:
# `-- ConfigurationManifestReader
#     +-- default_manifest_path(home_directory)
#     +-- parse_time_span_ms(value)
#     +-- read_application(manifest_path, application_name)
#     +-- read_manifest(manifest_path)
#     `-- strip_jsonc(raw)
"""Manifest reader supporting JSONC comments, private-file verification, and lockTimeoutMs."""

import json
import os
from pathlib import Path
import re
from typing import Final

from nyssa_ai_configuration.file_security.directory_safety import DirectorySafety
from nyssa_ai_configuration.manifest.types import (
    ApplicationManifestEntry,
    ConfigurationManifestDocument,
    VaultOptions,
)
from nyssa_ai_configuration.results.result import Result, fail, ok

_DEFAULT_MANIFEST_FILENAME: Final[str] = "config-manifest.json"
_DEFAULT_MANIFEST_FOLDER: Final[str] = ".nyssa-ai"
_DEFAULT_LOCK_TIMEOUT_MS: Final[int] = 10000
_SUPPORTED_VERSION: Final[int] = 1
_TIMESPAN_RE: Final[re.Pattern[str]] = re.compile(r"^(?:(\d+)\.)?(\d{1,2}):(\d{2}):(\d{2})(?:\.(\d+))?$")


class ConfigurationManifestReader:
    """Reads and validates application sections from config-manifest.json."""

    @staticmethod
    def default_manifest_path(home_directory: str | Path | None = None) -> Path:
        """Return the canonical manifest path under home_directory or Path.home()."""
        home = Path(home_directory) if home_directory is not None and str(home_directory).strip() else Path.home()
        return (home / _DEFAULT_MANIFEST_FOLDER / _DEFAULT_MANIFEST_FILENAME).resolve()

    @staticmethod
    def strip_jsonc(raw: str) -> str:
        """Strip // line comments, /* */ block comments, and trailing commas outside string literals."""
        out: list[str] = []
        in_str = False
        escape = False
        i = 0
        n = len(raw)
        while i < n:
            ch = raw[i]
            if in_str:
                out.append(ch)
                if escape:
                    escape = False
                elif ch == "\\":
                    escape = True
                elif ch == '"':
                    in_str = False
                i += 1
                continue
            if ch == '"':
                in_str = True
                out.append(ch)
                i += 1
                continue
            if ch == "/" and i + 1 < n and raw[i + 1] == "/":
                i += 2
                while i < n and raw[i] not in ("\n", "\r"):
                    i += 1
                continue
            if ch == "/" and i + 1 < n and raw[i + 1] == "*":
                i += 2
                while i + 1 < n and not (raw[i] == "*" and raw[i + 1] == "/"):
                    i += 1
                i += 2
                continue
            out.append(ch)
            i += 1
        cleaned = "".join(out)
        return re.sub(r",(\s*[}\]])", r"\1", cleaned)

    @staticmethod
    def parse_time_span_ms(value: str) -> Result[int]:
        """Parse a legacy .NET TimeSpan string ('00:00:10') into positive integer milliseconds."""
        match = _TIMESPAN_RE.match(value.strip())
        if not match:
            return fail(f"Invalid TimeSpan format: '{value}'", "Validation")
        days = int(match.group(1) or 0)
        hours = int(match.group(2))
        minutes = int(match.group(3))
        seconds = int(match.group(4))
        if hours > 23 or minutes > 59 or seconds > 59:
            return fail(f"Out-of-range TimeSpan components in '{value}'", "Validation")
        frac = match.group(5) or "0"
        frac_ms = int(frac[:3].ljust(3, "0")) if frac else 0
        total_ms = (days * 86400 + hours * 3600 + minutes * 60 + seconds) * 1000 + frac_ms
        if total_ms <= 0:
            return fail("lockTimeout must be greater than zero.", "Validation")
        return ok(total_ms)

    @staticmethod
    def read_manifest(manifest_path: str | Path) -> Result[ConfigurationManifestDocument]:
        """Read and validate the entire manifest document at manifest_path."""
        text_path = str(manifest_path).strip()
        if not text_path:
            return fail("Manifest path cannot be blank.", "Validation")
        resolved = Path(text_path).resolve()
        if not resolved.exists():
            return fail(f"manifest: no manifest at '{manifest_path}'", "NotFound")

        file_safe = DirectorySafety.verify_file(resolved)
        if not file_safe.ok:
            return file_safe

        try:
            raw = resolved.read_text(encoding="utf-8")
            parsed: object = json.loads(ConfigurationManifestReader.strip_jsonc(raw))
        except Exception as exc:
            return fail(f"manifest: '{manifest_path}' failed JSON validation: {exc}", "Validation", exc)

        if not isinstance(parsed, dict) or parsed.get("version") != _SUPPORTED_VERSION:
            return fail(
                f"manifest: '{manifest_path}' is not a version {_SUPPORTED_VERSION} manifest",
                "Validation",
            )

        apps_raw = parsed.get("applications")
        if not isinstance(apps_raw, dict):
            return fail(f"manifest: '{manifest_path}' requires an 'applications' object", "Validation")

        apps: dict[str, ApplicationManifestEntry] = {}
        has_secrets = False
        for app_name, section in apps_raw.items():
            if not isinstance(app_name, str) or not app_name.strip() or not isinstance(section, dict):
                return fail("manifest: application entry must be a non-empty object", "Validation")
            entry_res = ConfigurationManifestReader._parse_section(resolved, app_name, section)
            if not entry_res.ok:
                return entry_res
            if entry_res.value.key_vault is not None and len(entry_res.value.key_vault.secrets) > 0:
                has_secrets = True
            apps[app_name] = entry_res.value

        if has_secrets:
            priv_res = DirectorySafety.verify_private_file(resolved)
            if not priv_res.ok:
                return priv_res

        return ok(ConfigurationManifestDocument(version=_SUPPORTED_VERSION, applications=apps))

    @staticmethod
    def read_application(manifest_path: str | Path, application_name: str) -> Result[ApplicationManifestEntry]:
        """Read a single application's bootstrap section from manifest_path."""
        if not application_name or not application_name.strip():
            return fail("Application name cannot be blank.", "Validation")
        doc_res = ConfigurationManifestReader.read_manifest(manifest_path)
        if not doc_res.ok:
            return doc_res
        entry = doc_res.value.applications.get(application_name)
        if entry is None:
            return fail(f"manifest: '{manifest_path}' has no section for '{application_name}'", "NotFound")
        return ok(entry)

    @staticmethod
    def _parse_section(
        manifest_file: Path,
        app_name: str,
        section: dict[object, object],
    ) -> Result[ApplicationManifestEntry]:
        config_dir = section.get("configurationDirectory")
        if not isinstance(config_dir, str) or not config_dir.strip():
            return fail(f"manifest: '{app_name}' requires a non-empty 'configurationDirectory'", "Validation")

        timeout_res = ConfigurationManifestReader._parse_timeout(app_name, section)
        if not timeout_res.ok:
            return timeout_res

        resolved_dir = (
            Path(config_dir).resolve()
            if os.path.isabs(config_dir)
            else (manifest_file.parent / config_dir).resolve()
        )

        vault_raw = section.get("keyVault")
        vault_opt: VaultOptions | None = None
        if vault_raw is not None:
            vault_res = ConfigurationManifestReader._parse_vault(app_name, vault_raw)
            if not vault_res.ok:
                return vault_res
            vault_opt = vault_res.value

        return ok(
            ApplicationManifestEntry(
                configuration_directory=str(resolved_dir),
                lock_timeout_ms=timeout_res.value,
                key_vault=vault_opt,
            )
        )

    @staticmethod
    def _parse_timeout(app_name: str, section: dict[object, object]) -> Result[int]:
        if "lockTimeoutMs" in section and section["lockTimeoutMs"] is not None:
            ms_val = section["lockTimeoutMs"]
            if isinstance(ms_val, int) and not isinstance(ms_val, bool) and ms_val > 0:
                return ok(ms_val)
            return fail(f"manifest: '{app_name}' lockTimeoutMs must be a positive integer", "Validation")
        if "lockTimeout" in section and section["lockTimeout"] is not None:
            raw_val = section["lockTimeout"]
            if isinstance(raw_val, int) and not isinstance(raw_val, bool) and raw_val > 0:
                return ok(raw_val)
            if isinstance(raw_val, str) and raw_val.strip():
                return ConfigurationManifestReader.parse_time_span_ms(raw_val)
            return fail(f"manifest: '{app_name}' lockTimeout is invalid", "Validation")
        return ok(_DEFAULT_LOCK_TIMEOUT_MS)

    @staticmethod
    def _parse_vault(app_name: str, vault_raw: object) -> Result[VaultOptions]:
        if not isinstance(vault_raw, dict):
            return fail(f"manifest: '{app_name}' keyVault must be an object", "Validation")
        name = vault_raw.get("name")
        kind = vault_raw.get("kind")
        if not isinstance(name, str) or not name.strip() or not isinstance(kind, str) or not kind.strip():
            return fail(f"manifest: '{app_name}' keyVault requires non-empty 'name' and 'kind'", "Validation")
        endpoint = vault_raw.get("endpoint")
        if endpoint is not None and (not isinstance(endpoint, str) or not endpoint.strip()):
            return fail(f"manifest: '{app_name}' keyVault endpoint must be a non-empty string", "Validation")
        credential = vault_raw.get("credential")
        if credential is not None and (not isinstance(credential, str) or "/" not in credential.strip("/")):
            return fail(
                f"manifest: '{app_name}' keyVault credential must be formatted as 'namespace/key'",
                "Validation",
            )
        settings_map = ConfigurationManifestReader._parse_str_map(vault_raw.get("settings"), "settings")
        if not settings_map.ok:
            return settings_map
        secrets_map = ConfigurationManifestReader._parse_str_map(vault_raw.get("secrets"), "secrets")
        if not secrets_map.ok:
            return secrets_map
        return ok(
            VaultOptions(
                name=name.strip(),
                kind=kind.strip(),
                endpoint=endpoint.strip() if isinstance(endpoint, str) else None,
                credential=credential.strip() if isinstance(credential, str) else None,
                settings=settings_map.value,
                secrets=secrets_map.value,
            )
        )

    @staticmethod
    def _parse_str_map(raw_map: object, field_name: str) -> Result[dict[str, str]]:
        if raw_map is None:
            return ok({})
        if not isinstance(raw_map, dict):
            return fail(f"manifest: keyVault '{field_name}' must be a string map", "Validation")
        out: dict[str, str] = {}
        for k, v in raw_map.items():
            if not isinstance(k, str) or not k.strip() or not isinstance(v, str):
                return fail(f"manifest: keyVault '{field_name}' entries must be non-empty strings", "Validation")
            out[k] = v
        return ok(out)
