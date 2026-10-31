# Responsibility: Verify ConfigurationValueCodec wire serialization and deserialization rules matching .NET ConfigurationValueJsonConverter.
# Public interface:
# +-- test_codec_emits_json_flag_for_encrypted_json_and_quoted_json_literals()
# +-- test_codec_omits_encrypted_on_vault_pointers_and_omits_json_on_plain_json_objects()
# `-- test_codec_rejects_encrypted_without_aes_local_provider()
"""Unit tests for ConfigurationValueCodec cross-runtime JSON wire parity."""

from nyssa_ai_configuration.values.configuration_value_codec import (
    ConfigurationValueCodec,
)
from nyssa_ai_configuration.values.types import ConfigurationValue


def test_codec_omits_encrypted_on_vault_pointers_and_omits_json_on_plain_json_objects() -> None:
    """Verify vault pointers omit 'encrypted' and unquoted JSON object literals omit 'json': true."""
    entries = {
        "vaultSecret": ConfigurationValue(value="item-uuid-1", provider="bitwarden", encrypted=False),
        "plainObject": ConfigurationValue(value='{"host":"localhost","port":5432}', provider="", encrypted=False, is_json_value=True),
        "plainText": ConfigurationValue(value="hello", provider="", encrypted=False, is_json_value=False),
    }

    encoded = ConfigurationValueCodec.encode_document(entries)
    assert encoded.ok
    text = encoded.value

    assert '"provider": "bitwarden"' in text
    assert '"vaultSecret": {\n    "value": "item-uuid-1",\n    "provider": "bitwarden"\n  }' in text
    assert '"plainObject": {\n    "value": {\n      "host": "localhost",\n      "port": 5432\n    }\n  }' in text

    parsed = ConfigurationValueCodec.decode_document(text)
    assert parsed.ok
    assert parsed.value["vaultSecret"].provider == "bitwarden"
    assert parsed.value["vaultSecret"].encrypted is False
    assert parsed.value["plainObject"].is_json_value is True
    assert parsed.value["plainText"].is_json_value is False


def test_codec_emits_json_flag_for_encrypted_json_and_quoted_json_literals() -> None:
    """Verify 'json': true is emitted for encrypted JSON values and JSON string literals starting with quotes."""
    entries = {
        "encryptedJson": ConfigurationValue(value="ciphertext-1", provider="aesLocal", encrypted=True, is_json_value=True),
        "quotedJson": ConfigurationValue(value='"quoted-string"', provider="", encrypted=False, is_json_value=True),
    }

    encoded = ConfigurationValueCodec.encode_document(entries)
    assert encoded.ok
    text = encoded.value
    assert '"json": true' in text

    parsed = ConfigurationValueCodec.decode_document(text)
    assert parsed.ok
    assert parsed.value["encryptedJson"].is_json_value is True
    assert parsed.value["quotedJson"].is_json_value is True


def test_codec_rejects_encrypted_without_aes_local_provider() -> None:
    """Verify decode_document rejects entries with encrypted=true and no provider."""
    invalid_json = '{"bad": {"value": "abc", "encrypted": true}}'
    parsed = ConfigurationValueCodec.decode_document(invalid_json)

    assert not parsed.ok
    assert parsed.category == "Validation"
