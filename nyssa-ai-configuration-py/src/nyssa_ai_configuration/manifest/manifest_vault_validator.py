# Responsibility: Validate root keyVaults manifest entries, programmatic VaultOptions, and encrypted secret envelopes.
# Public interface:
# `-- ManifestVaultValidator
#     +-- has_any_secrets(parsed)
#     +-- parse_vault_entry(vault_key, vault_raw)
#     +-- validate_vault_name(vault_name)
#     `-- validate_vault_options(vault)
"""Manifest keyVaults and programmatic VaultOptions validation."""

import base64
import re
from typing import Final
import urllib.parse

from nyssa_ai_configuration.file_security.directory_safety import DirectorySafety
from nyssa_ai_configuration.manifest.types import ManifestSecretEnvelope, VaultManifestEntry, VaultOptions
from nyssa_ai_result import Result, fail, ok

_MIN_ENVELOPE_BYTES: Final[int] = 29
_FORMAT_VERSION_2: Final[int] = 0x02
_RESERVED_VAULT_NAMES: Final[frozenset[str]] = frozenset({"aeslocal", "literal", "oauth", "default", "plaintext"})
_CONTROL_CHAR_RE: Final[re.Pattern[str]] = re.compile(r"[\x00-\x1f\x7f]")
_SCHEME_RE: Final[re.Pattern[str]] = re.compile(r"^[A-Za-z][A-Za-z0-9+.-]*$")
_DIGITS_RE: Final[re.Pattern[str]] = re.compile(r"^[0-9]+$")
_BASE64URL_RE: Final[re.Pattern[str]] = re.compile(r"^[A-Za-z0-9_-]+$")


class ManifestVaultValidator:
    """Validates manifest keyVaults entries, programmatic VaultOptions, and encrypted secret envelopes."""

    @staticmethod
    def has_any_secrets(parsed: dict[object, object]) -> bool:
        """Return whether any root or legacy vault entry defines non-empty secrets."""
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
        name_res = ManifestVaultValidator.validate_vault_name(vault.name)
        if not name_res.ok:
            return name_res
        v_name = name_res.value
        common_res = ManifestVaultValidator._validate_vault_common(
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
    def parse_vault_entry(vault_key: object, vault_raw: object) -> Result[VaultManifestEntry]:
        """Validate and normalize one root keyVaults manifest entry."""
        name_res = ManifestVaultValidator.validate_vault_name(vault_key)
        if not name_res.ok:
            return name_res
        v_name = name_res.value
        if not isinstance(vault_raw, dict):
            return fail(f"manifest: keyVaults.{v_name} must be an object", "Validation")
        if "name" in vault_raw and vault_raw["name"] is not None:
            if not isinstance(vault_raw["name"], str) or vault_raw["name"] != v_name:
                return fail(f"manifest: keyVaults.{v_name}.name must match key '{v_name}'", "Validation")
        has_ttl = "cacheTTLSeconds" in vault_raw and vault_raw["cacheTTLSeconds"] is not None
        common_res = ManifestVaultValidator._validate_vault_common(
            v_name, vault_raw.get("kind"), vault_raw.get("endpoint"), vault_raw.get("credential"),
            vault_raw.get("settings"), has_ttl, vault_raw.get("cacheTTLSeconds"),
        )
        if not common_res.ok:
            return common_res
        kind_val, endpoint_val, cred_val, settings_val, ttl_val = common_res.value
        sec_res = ManifestVaultValidator._parse_secret_envelopes(v_name, vault_raw.get("secrets"))
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
            val_str = ManifestVaultValidator._extract_valid_envelope_value(env_raw)
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
