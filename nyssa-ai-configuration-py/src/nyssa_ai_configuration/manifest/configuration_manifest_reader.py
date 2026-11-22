# Responsibility: Read and validate config-manifest.json with JSONC stripping, private secret checks, and root keyVaults envelopes.
# Public interface:
# `-- ConfigurationManifestReader
#     +-- default_manifest_path(home_directory)
#     +-- parse_time_span_ms(value)
#     +-- read_application(manifest_path, application_name)
#     +-- read_manifest(manifest_path)
#     +-- strip_jsonc(raw)
#     +-- validate_vault_name(vault_name)
#     `-- validate_vault_options(vault)
"""Manifest reader supporting JSONC comments, private-file verification, and encrypted root keyVaults."""

import json
from pathlib import Path
import re
from typing import Final

from nyssa_ai_configuration.file_security.directory_safety import DirectorySafety
from nyssa_ai_configuration.manifest.application_manifest_entry_reader import ApplicationManifestEntryReader
from nyssa_ai_configuration.manifest.manifest_vault_validator import ManifestVaultValidator
from nyssa_ai_configuration.manifest.namespace_registry import NamespaceRegistry
from nyssa_ai_configuration.manifest.types import ApplicationManifestEntry, ConfigurationManifestDocument, VaultManifestEntry, VaultOptions
from nyssa_ai_result import Result, fail, ok

_DEFAULT_MANIFEST_FILENAME: Final[str] = "config-manifest.json"
_DEFAULT_MANIFEST_FOLDER: Final[str] = ".nyssa-ai"
_DEFAULT_LOCK_TIMEOUT_MS: Final[int] = 10000
_SUPPORTED_VERSION: Final[int] = 1
_TIMESPAN_RE: Final[re.Pattern[str]] = re.compile(r"^(?:(\d+)\.)?(\d{1,2}):(\d{2}):(\d{2})(?:\.(\d+))?$")


class ConfigurationManifestReader:
    """Reads and validates root keyVaults and application sections from config-manifest.json."""

    @staticmethod
    def default_manifest_path(home_directory: str | Path | None = None) -> Path:
        """Return the canonical manifest path under home_directory or Path.home()."""
        home = Path(home_directory) if home_directory is not None and str(home_directory).strip() else Path.home()
        return (home / _DEFAULT_MANIFEST_FOLDER / _DEFAULT_MANIFEST_FILENAME).resolve()

    @staticmethod
    def strip_jsonc(raw: str) -> str:
        """Strip // line comments, /* */ block comments, and trailing commas outside string literals."""
        out: list[str] = []
        in_str, escape, i, n = False, False, 0, len(raw)
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
            elif ch == '"':
                in_str = True
                out.append(ch)
                i += 1
            elif ch == "/" and i + 1 < n and raw[i + 1] == "/":
                i += 2
                while i < n and raw[i] not in ("\n", "\r"):
                    i += 1
            elif ch == "/" and i + 1 < n and raw[i + 1] == "*":
                i += 2
                while i + 1 < n and not (raw[i] == "*" and raw[i + 1] == "/"):
                    i += 1
                i += 2
            else:
                out.append(ch)
                i += 1
        return re.sub(r",(\s*[}\]])", r"\1", "".join(out))

    @staticmethod
    def parse_time_span_ms(value: str) -> Result[int]:
        """Parse a legacy .NET TimeSpan string ('00:00:10') into positive integer milliseconds."""
        match = _TIMESPAN_RE.match(value.strip())
        if not match:
            return fail(f"Invalid TimeSpan format: '{value}'", "Validation")
        days, hours = int(match.group(1) or 0), int(match.group(2))
        minutes, seconds = int(match.group(3)), int(match.group(4))
        if hours > 23 or minutes > 59 or seconds > 59:
            return fail(f"Out-of-range TimeSpan components in '{value}'", "Validation")
        frac = match.group(5) or "0"
        frac_ms = int(frac[:3].ljust(3, "0")) if frac else 0
        total_ms = (days * 86400 + hours * 3600 + minutes * 60 + seconds) * 1000 + frac_ms
        return ok(total_ms) if total_ms > 0 else fail("lockTimeout must be greater than zero.", "Validation")

    @staticmethod
    def validate_vault_name(vault_name: object) -> Result[str]:
        """Validate a vault identifier against whitespace, path separators, control chars, and reserved names."""
        return ManifestVaultValidator.validate_vault_name(vault_name)

    @staticmethod
    def validate_vault_options(vault: VaultOptions) -> Result[VaultOptions]:
        """Validate and normalize programmatic VaultOptions using canonical manifest vault rules."""
        return ManifestVaultValidator.validate_vault_options(vault)

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

        if isinstance(parsed, dict) and ManifestVaultValidator.has_any_secrets(parsed):
            priv_res = DirectorySafety.verify_private_file(resolved)
            if not priv_res.ok:
                return priv_res

        if not isinstance(parsed, dict) or parsed.get("version") != _SUPPORTED_VERSION:
            return fail(f"manifest: '{manifest_path}' is not a version {_SUPPORTED_VERSION} manifest", "Validation")

        namespace_registry_res = NamespaceRegistry.parse(resolved, parsed.get("namespaceRegistry"))
        if not namespace_registry_res.ok:
            return namespace_registry_res
        namespace_registry = namespace_registry_res.value

        apps_raw = {} if parsed.get("applications") is None else parsed.get("applications")
        if not isinstance(apps_raw, dict):
            return fail(f"manifest: '{manifest_path}' requires an 'applications' object", "Validation")
        apps: dict[str, ApplicationManifestEntry] = {}
        for app_name, section in apps_raw.items():
            if not isinstance(app_name, str) or not app_name.strip() or not isinstance(section, dict):
                return fail("manifest: application entry must be a non-empty object", "Validation")
            entry_res = ApplicationManifestEntryReader.parse(
                resolved, app_name, section, namespace_registry, ConfigurationManifestReader._parse_timeout
            )
            if not entry_res.ok:
                return entry_res
            apps[app_name] = entry_res.value

        kv_raw = {} if parsed.get("keyVaults") is None else parsed.get("keyVaults")
        if not isinstance(kv_raw, dict):
            return fail(f"manifest: '{manifest_path}' 'keyVaults' must be an object", "Validation")
        vaults: dict[str, VaultManifestEntry] = {}
        for vault_name, vault_obj in kv_raw.items():
            v_res = ManifestVaultValidator.parse_vault_entry(vault_name, vault_obj)
            if not v_res.ok:
                return v_res
            vaults[v_res.value.name] = v_res.value

        return ok(ConfigurationManifestDocument(
            version=_SUPPORTED_VERSION,
            applications=apps,
            key_vaults=vaults,
            namespace_registry=namespace_registry,
        ))

    @staticmethod
    def read_application(manifest_path: str | Path, application_name: str) -> Result[ApplicationManifestEntry]:
        """Read a single application's bootstrap section from manifest_path."""
        if not application_name or not application_name.strip():
            return fail("Application name cannot be blank.", "Validation")
        doc_res = ConfigurationManifestReader.read_manifest(manifest_path)
        if not doc_res.ok:
            return doc_res
        entry = doc_res.value.applications.get(application_name)
        return ok(entry) if entry is not None else fail(f"manifest: '{manifest_path}' has no section for '{application_name}'", "NotFound")

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
