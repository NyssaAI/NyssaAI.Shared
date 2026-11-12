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

import base64
import json
import os
from pathlib import Path
import re
from typing import Final
import urllib.parse

from nyssa_ai_configuration.file_security.directory_safety import DirectorySafety
from nyssa_ai_configuration.manifest.types import ApplicationManifestEntry, ConfigurationManifestDocument, ManifestSecretEnvelope, VaultManifestEntry, VaultOptions
from nyssa_ai_result import Result, fail, ok

_DEFAULT_MANIFEST_FILENAME: Final[str] = "config-manifest.json"
_DEFAULT_MANIFEST_FOLDER: Final[str] = ".nyssa-ai"
_DEFAULT_LOCK_TIMEOUT_MS: Final[int] = 10000
_SUPPORTED_VERSION: Final[int] = 1
_MIN_ENVELOPE_BYTES: Final[int] = 29
_FORMAT_VERSION_2: Final[int] = 0x02
_RESERVED_VAULT_NAMES: Final[frozenset[str]] = frozenset({"aeslocal", "literal", "oauth", "default", "plaintext"})
_TIMESPAN_RE: Final[re.Pattern[str]] = re.compile(r"^(?:(\d+)\.)?(\d{1,2}):(\d{2}):(\d{2})(?:\.(\d+))?$")
_CONTROL_CHAR_RE: Final[re.Pattern[str]] = re.compile(r"[\x00-\x1f\x7f]")
_SCHEME_RE: Final[re.Pattern[str]] = re.compile(r"^[A-Za-z][A-Za-z0-9+.-]*$")
_DIGITS_RE: Final[re.Pattern[str]] = re.compile(r"^[0-9]+$")
_BASE64URL_RE: Final[re.Pattern[str]] = re.compile(r"^[A-Za-z0-9_-]+$")


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
        if not isinstance(vault_name, str) or not vault_name.strip():
            return fail("manifest: keyVault name must be a non-empty string", "Validation")
        if vault_name.strip() != vault_name or any(ch.isspace() for ch in vault_name):
            return fail(f"manifest: keyVault name '{vault_name}' must not contain whitespace", "Validation")
        if "/" in vault_name or "\\" in vault_name or _CONTROL_CHAR_RE.search(vault_name):
            return fail("manifest: keyVault name contains forbidden characters", "Validation")
        if vault_name.lower() in _RESERVED_VAULT_NAMES:
            return fail(f"manifest: keyVault name '{vault_name}' is reserved", "Validation")
        return ok(vault_name)

    @staticmethod
    def validate_vault_options(vault: VaultOptions) -> Result[VaultOptions]:
        """Validate and normalize programmatic VaultOptions using canonical manifest vault rules."""
        name_res = ConfigurationManifestReader.validate_vault_name(vault.name)
        if not name_res.ok:
            return name_res
        v_name = name_res.value
        common_res = ConfigurationManifestReader._validate_vault_common(
            v_name, vault.kind, vault.endpoint, vault.credential, vault.settings,
            vault.cache_ttl_seconds is not None, vault.cache_ttl_seconds,
        )
        if not common_res.ok:
            return common_res
        kind_val, endpoint_val, cred_val, settings_val, ttl_val = common_res.value
        secrets_out: dict[str, str] = {}
        for sk, sv in vault.secrets.items():
            if not isinstance(sk, str) or not sk.strip() or sk.strip() != sk or _CONTROL_CHAR_RE.search(sk) or not isinstance(sv, str):
                return fail(f"manifest: keyVaults.{v_name}.secrets has an invalid key or value", "Validation")
            secrets_out[sk] = sv
        return ok(VaultOptions(v_name, kind_val, endpoint_val, cred_val, settings_val, secrets_out, ttl_val))

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

        if isinstance(parsed, dict) and ConfigurationManifestReader._has_any_secrets(parsed):
            priv_res = DirectorySafety.verify_private_file(resolved)
            if not priv_res.ok:
                return priv_res

        if not isinstance(parsed, dict) or parsed.get("version") != _SUPPORTED_VERSION:
            return fail(f"manifest: '{manifest_path}' is not a version {_SUPPORTED_VERSION} manifest", "Validation")

        apps_raw = {} if parsed.get("applications") is None else parsed.get("applications")
        if not isinstance(apps_raw, dict):
            return fail(f"manifest: '{manifest_path}' requires an 'applications' object", "Validation")
        apps: dict[str, ApplicationManifestEntry] = {}
        for app_name, section in apps_raw.items():
            if not isinstance(app_name, str) or not app_name.strip() or not isinstance(section, dict):
                return fail("manifest: application entry must be a non-empty object", "Validation")
            entry_res = ConfigurationManifestReader._parse_section(resolved, app_name, section)
            if not entry_res.ok:
                return entry_res
            apps[app_name] = entry_res.value

        kv_raw = {} if parsed.get("keyVaults") is None else parsed.get("keyVaults")
        if not isinstance(kv_raw, dict):
            return fail(f"manifest: '{manifest_path}' 'keyVaults' must be an object", "Validation")
        vaults: dict[str, VaultManifestEntry] = {}
        for vault_name, vault_obj in kv_raw.items():
            v_res = ConfigurationManifestReader._parse_vault_entry(vault_name, vault_obj)
            if not v_res.ok:
                return v_res
            vaults[v_res.value.name] = v_res.value

        return ok(ConfigurationManifestDocument(version=_SUPPORTED_VERSION, applications=apps, key_vaults=vaults))

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
    def _has_any_secrets(parsed: dict[object, object]) -> bool:
        def _non_empty_secrets(v_obj: object) -> bool:
            if not isinstance(v_obj, dict) or "secrets" not in v_obj:
                return False
            sec = v_obj["secrets"]
            return sec is not None and (not isinstance(sec, dict) or len(sec) > 0)

        kv_obj, apps_obj = parsed.get("keyVaults"), parsed.get("applications")
        if isinstance(kv_obj, dict) and any(_non_empty_secrets(v) for v in kv_obj.values()):
            return True
        return isinstance(apps_obj, dict) and any(
            isinstance(a, dict) and _non_empty_secrets(a.get("keyVault")) for a in apps_obj.values()
        )

    @staticmethod
    def _parse_section(manifest_file: Path, app_name: str, section: dict[object, object]) -> Result[ApplicationManifestEntry]:
        if section.get("keyVault") is not None:
            return fail(
                f"manifest: applications.{app_name}.keyVault is no longer supported; move vault definitions to root 'keyVaults'",
                "Validation",
            )
        config_dir = section.get("configurationDirectory")
        if not isinstance(config_dir, str) or not config_dir.strip():
            return fail(f"manifest: '{app_name}' requires a non-empty 'configurationDirectory'", "Validation")
        timeout_res = ConfigurationManifestReader._parse_timeout(app_name, section)
        if not timeout_res.ok:
            return timeout_res
        resolved_dir = Path(config_dir).resolve() if os.path.isabs(config_dir) else (manifest_file.parent / config_dir).resolve()
        return ok(ApplicationManifestEntry(configuration_directory=str(resolved_dir), lock_timeout_ms=timeout_res.value))

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
    def _parse_vault_entry(vault_key: object, vault_raw: object) -> Result[VaultManifestEntry]:
        name_res = ConfigurationManifestReader.validate_vault_name(vault_key)
        if not name_res.ok:
            return name_res
        v_name = name_res.value
        if not isinstance(vault_raw, dict):
            return fail(f"manifest: keyVaults.{v_name} must be an object", "Validation")
        if "name" in vault_raw and vault_raw["name"] is not None:
            if not isinstance(vault_raw["name"], str) or vault_raw["name"] != v_name:
                return fail(f"manifest: keyVaults.{v_name}.name must match key '{v_name}'", "Validation")
        has_ttl = "cacheTTLSeconds" in vault_raw and vault_raw["cacheTTLSeconds"] is not None
        common_res = ConfigurationManifestReader._validate_vault_common(
            v_name, vault_raw.get("kind"), vault_raw.get("endpoint"), vault_raw.get("credential"),
            vault_raw.get("settings"), has_ttl, vault_raw.get("cacheTTLSeconds"),
        )
        if not common_res.ok:
            return common_res
        kind_val, endpoint_val, cred_val, settings_val, ttl_val = common_res.value
        sec_res = ConfigurationManifestReader._parse_secret_envelopes(v_name, vault_raw.get("secrets"))
        if not sec_res.ok:
            return sec_res
        return ok(VaultManifestEntry(v_name, kind_val, endpoint_val, cred_val, settings_val, sec_res.value, ttl_val))

    @staticmethod
    def _validate_vault_common(
        v_name: str, kind: object, endpoint: object, credential: object,
        raw_settings: object, has_ttl: bool, raw_ttl: object,
    ) -> Result[tuple[str, str | None, str | None, dict[str, str], int | None]]:
        if not isinstance(kind, str) or not kind.strip() or _CONTROL_CHAR_RE.search(kind):
            return fail(f"manifest: keyVaults.{v_name} requires a valid non-empty 'kind'", "Validation")
        endpoint_str: str | None = None
        if endpoint is not None:
            if not isinstance(endpoint, str) or not endpoint.strip() or _CONTROL_CHAR_RE.search(endpoint):
                return fail(f"manifest: keyVaults.{v_name}.endpoint must be an absolute URL", "Validation")
            ep_trimmed = endpoint.strip()
            split_url = urllib.parse.urlsplit(ep_trimmed)
            if not split_url.scheme or not split_url.netloc or not _SCHEME_RE.fullmatch(split_url.scheme) or any(ch.isspace() for ch in ep_trimmed):
                return fail(f"manifest: keyVaults.{v_name}.endpoint must be an absolute URL", "Validation")
            endpoint_str = ep_trimmed
        cred_str: str | None = None
        if credential is not None:
            if not isinstance(credential, str) or not credential.strip() or _CONTROL_CHAR_RE.search(credential):
                return fail(f"manifest: keyVaults.{v_name}.credential must be formatted as 'namespace/key'", "Validation")
            cred_trimmed = credential.strip()
            parts = cred_trimmed.removeprefix("oauth://").split("/")
            if len(parts) != 2:
                return fail(f"manifest: keyVaults.{v_name}.credential must be formatted as 'namespace/key'", "Validation")
            for seg, lbl in ((parts[0], "credential namespace"), (parts[1], "credential key")):
                seg_res = DirectorySafety.validate_segment(seg, lbl)
                if not seg_res.ok:
                    return seg_res
            cred_str = cred_trimmed
        settings_out: dict[str, str] = {}
        if raw_settings is not None:
            if not isinstance(raw_settings, dict):
                return fail(f"manifest: keyVaults.{v_name}.settings must be a string map", "Validation")
            for k, v in raw_settings.items():
                if not isinstance(k, str) or not k.strip() or _CONTROL_CHAR_RE.search(k) or not isinstance(v, str):
                    return fail(f"manifest: keyVaults.{v_name}.settings entries must be non-empty strings", "Validation")
                settings_out[k] = v
        ttl_out: int | None = None
        if has_ttl:
            if not isinstance(raw_ttl, int) or isinstance(raw_ttl, bool) or raw_ttl < 0:
                return fail(f"manifest: keyVaults.{v_name}.cacheTTLSeconds must be a non-negative integer", "Validation")
            ttl_out = raw_ttl
        if "cacheTTLSeconds" in settings_out:
            s_ttl = settings_out["cacheTTLSeconds"].strip()
            if not _DIGITS_RE.fullmatch(s_ttl):
                return fail(f"manifest: keyVaults.{v_name}.settings['cacheTTLSeconds'] must be a non-negative integer", "Validation")
            if ttl_out is None:
                ttl_out = int(s_ttl)
        return ok((kind.strip(), endpoint_str, cred_str, settings_out, ttl_out))

    @staticmethod
    def _parse_secret_envelopes(v_name: str, raw_secrets: object) -> Result[dict[str, ManifestSecretEnvelope]]:
        if raw_secrets is None:
            return ok({})
        if not isinstance(raw_secrets, dict):
            return fail(f"manifest: keyVaults.{v_name}.secrets must be an object", "Validation")
        envelopes: dict[str, ManifestSecretEnvelope] = {}
        for sk, env_raw in raw_secrets.items():
            if not isinstance(sk, str) or not sk.strip() or sk.strip() != sk or _CONTROL_CHAR_RE.search(sk):
                return fail(f"manifest: keyVaults.{v_name}.secrets key must be a valid non-empty string", "Validation")
            val_str = ConfigurationManifestReader._extract_valid_envelope_value(env_raw)
            if val_str is None:
                return fail(
                    f"manifest: keyVaults.{v_name}.secrets.{sk} must be an encrypted aesLocal envelope; plaintext secrets are forbidden in config-manifest.json",
                    "Validation",
                )
            envelopes[sk] = ManifestSecretEnvelope(value=val_str, encrypted=True, provider="aesLocal")
        return ok(envelopes)

    @staticmethod
    def _extract_valid_envelope_value(env_raw: object) -> str | None:
        if not isinstance(env_raw, dict):
            return None
        enc, prov, val = env_raw.get("encrypted"), env_raw.get("provider"), env_raw.get("value")
        if not (isinstance(enc, bool) and enc is True):
            return None
        if prov is not None and not (isinstance(prov, str) and prov == "aesLocal"):
            return None
        if not isinstance(val, str) or _BASE64URL_RE.fullmatch(val) is None or len(val) % 4 == 1:
            return None
        try:
            decoded = base64.urlsafe_b64decode((val + "=" * ((4 - len(val) % 4) % 4)).encode("ascii"))
        except Exception:
            return None
        return val if len(decoded) >= _MIN_ENVELOPE_BYTES and decoded[0] == _FORMAT_VERSION_2 else None
