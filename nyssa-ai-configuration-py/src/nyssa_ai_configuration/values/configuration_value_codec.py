# Responsibility: Encode and decode ConfigurationValue entries matching .NET ConfigurationValueJsonConverter wire rules.
# Public interface:
# `-- ConfigurationValueCodec
#     +-- decode_document(raw_json)
#     +-- decode_entry(raw_entry)
#     +-- encode_document(entries)
#     +-- encode_entry(entry)
#     `-- validate_entry(entry)
"""Wire-compatible JSON encoder/decoder for <namespace>.json documents."""

from collections.abc import Mapping
import json

from nyssa_ai_configuration.results.result import Result, fail, ok
from nyssa_ai_configuration.values.types import ConfigurationValue


class ConfigurationValueCodec:
    """Serializes and deserializes ConfigurationValue entries with exact .NET wire parity."""

    @staticmethod
    def validate_entry(entry: ConfigurationValue) -> Result[None]:
        """Validate provider and encryption invariants for a ConfigurationValue."""
        if entry.is_literal and entry.encrypted:
            return fail("Encrypted entries require a non-empty provider (such as 'aesLocal' or 'oauth').", "Validation")
        if entry.provider in ("aesLocal", "oauth") and not entry.encrypted:
            return fail(f"Provider '{entry.provider}' entries must have encrypted=True.", "Validation")
        return ok(None)

    @staticmethod
    def decode_entry(raw_entry: object) -> Result[ConfigurationValue]:
        """Decode a single JSON object into a ConfigurationValue."""
        if not isinstance(raw_entry, dict):
            return fail("The configuration entry must be a JSON object.", "Validation")
        if "value" not in raw_entry:
            return fail("The configuration entry requires a value.", "Validation")
        raw_val = raw_entry["value"]
        if raw_val is None and not isinstance(raw_entry.get("json"), bool):
            # Null JSON value in a structured plaintext entry
            pass

        provider_raw = raw_entry.get("provider", "")
        if provider_raw is not None and not isinstance(provider_raw, str):
            return fail("The configuration entry 'provider' must be a string.", "Validation")
        provider = provider_raw or ""

        encrypted_raw = raw_entry.get("encrypted", False)
        if not isinstance(encrypted_raw, bool):
            return fail("The configuration entry 'encrypted' flag must be a boolean.", "Validation")
        encrypted = encrypted_raw

        reauth_raw = raw_entry.get("reauth_required", False)
        if not isinstance(reauth_raw, bool):
            return fail("The configuration entry 'reauth_required' flag must be a boolean.", "Validation")
        reauth_required = reauth_raw

        json_raw = raw_entry.get("json", False)
        if not isinstance(json_raw, bool):
            return fail("The configuration entry 'json' flag must be a boolean.", "Validation")

        is_object = not isinstance(raw_val, str)
        is_json = is_object or json_raw
        if is_object and (encrypted or len(provider) != 0):
            return fail("Structured configuration values must use plaintext storage.", "Validation")

        if is_json and len(provider) == 0:
            str_value = json.dumps(raw_val, separators=(",", ":"))
        else:
            assert isinstance(raw_val, str)
            str_value = raw_val

        entry = ConfigurationValue(
            value=str_value,
            provider=provider,
            encrypted=encrypted,
            reauth_required=reauth_required,
            is_json_value=is_json,
        )
        valid = ConfigurationValueCodec.validate_entry(entry)
        if not valid.ok:
            return valid
        return ok(entry)

    @staticmethod
    def encode_entry(entry: ConfigurationValue) -> Result[dict[str, object]]:
        """Encode a ConfigurationValue into its compact on-disk JSON dictionary."""
        out: dict[str, object] = {}
        if entry.is_json_value and entry.is_literal and not entry.encrypted:
            try:
                parsed_val: object = json.loads(entry.value)
            except Exception as exc:
                return fail(f"Invalid JSON value in configuration entry: {exc}", "Validation", exc)
            out["value"] = parsed_val
        else:
            out["value"] = entry.value

        if entry.encrypted:
            out["encrypted"] = True
        if not entry.is_literal:
            out["provider"] = entry.provider
        if entry.reauth_required:
            out["reauth_required"] = True
        if entry.is_json_value and (not entry.is_literal or entry.value.lstrip().startswith('"')):
            out["json"] = True
        return ok(out)

    @staticmethod
    def decode_document(raw_json: str) -> Result[dict[str, ConfigurationValue]]:
        """Decode a full <namespace>.json document into an ordered dictionary of ConfigurationValues."""
        if not raw_json or not raw_json.strip():
            return ok({})
        try:
            parsed: object = json.loads(raw_json)
        except Exception as exc:
            return fail(f"The configuration document is not valid JSON: {exc}", "Validation", exc)
        if not isinstance(parsed, dict):
            return fail("The configuration document root must be a JSON object.", "Validation")
        entries: dict[str, ConfigurationValue] = {}
        for k, v in parsed.items():
            if not isinstance(k, str) or not k.strip():
                return fail("Configuration keys must be non-empty strings.", "Validation")
            decoded = ConfigurationValueCodec.decode_entry(v)
            if not decoded.ok:
                return decoded
            entries[k] = decoded.value
        return ok(entries)

    @staticmethod
    def encode_document(entries: Mapping[str, ConfigurationValue]) -> Result[str]:
        """Encode a mapping of ConfigurationValues into formatted JSON text."""
        root: dict[str, object] = {}
        for k, entry in entries.items():
            encoded = ConfigurationValueCodec.encode_entry(entry)
            if not encoded.ok:
                return encoded
            root[k] = encoded.value
        return ok(json.dumps(root, indent=2) + "\n")
