# Responsibility: Verify ConfigurationManifestReader and ConfigurationManifestWriter root keyVaults, encryption envelopes, 0600 checks, and legacy rejection.
# Public interface:
# +-- test_manifest_0644_with_secrets_fails_forbidden_first_without_leaking_secret(tmp_path)
# +-- test_manifest_reader_parses_jsonc_legacy_timespan_and_root_key_vaults(tmp_path)
# +-- test_manifest_reader_rejects_invalid_vault_names_fields_and_unencrypted_secrets(tmp_path)
# +-- test_manifest_reader_rejects_legacy_per_application_key_vault(tmp_path)
# +-- test_manifest_repr_redacts_secret_and_envelope_values()
# `-- test_manifest_writer_set_vault_preserve_secrets_remove_vault_and_compact_json(tmp_path)
"""Unit tests for ConfigurationManifestReader and ConfigurationManifestWriter."""

import base64
import json
import os
from pathlib import Path
import sys

import pytest

from nyssa_ai_configuration import (
    MANIFEST_KEY_ACCOUNT,
    ConfigurationCipherAesGcm,
    ConfigurationManifestReader,
    ConfigurationManifestWriter,
    KeyringInMemory,
    ManifestSecretEnvelope,
    ProtectedFileSystem,
    VaultManifestEntry,
    VaultOptions,
)


def test_manifest_reader_parses_jsonc_legacy_timespan_and_root_key_vaults(tmp_path: Path) -> None:
    """Verify ConfigurationManifestReader strips JSONC, parses legacy lockTimeout, and validates encrypted root keyVaults."""
    manifest_path = tmp_path / "config-manifest.json"
    master_key = os.urandom(32)
    cipher_res = ConfigurationCipherAesGcm.create(master_key)
    assert cipher_res.ok
    sealed_res = cipher_res.value.seal("bws-corp-secret", "keyVaults/corp-vault", "accessToken")
    assert sealed_res.ok
    sealed_tok = sealed_res.value

    manifest_text = f"""
    {{
      // Application configuration root with comments
      "version": 1,
      "keyVaults": {{
        "corp-vault": {{
          "kind": "bitwarden-secrets",
          "endpoint": "https://vault.nyssa.example",
          "credential": "oauth://github/pat",
          "cacheTTLSeconds": 120,
          "settings": {{
            "organizationId": "org-1",
          }},
          "secrets": {{
            "accessToken": {{
              "value": "{sealed_tok}",
              "provider": "aesLocal",
              "encrypted": true,
            }},
          }},
        }},
      }},
      "applications": {{
        "nyssa": {{
          "configurationDirectory": "./config-store",
          "lockTimeout": "00:00:12.500", /* Legacy .NET TimeSpan */
        }},
      }},
    }}
    """
    assert ProtectedFileSystem.create_file(manifest_path, manifest_text).ok

    doc_res = ConfigurationManifestReader.read_manifest(manifest_path)
    assert doc_res.ok
    doc = doc_res.value

    assert "corp-vault" in doc.key_vaults
    vault = doc.key_vaults["corp-vault"]
    assert vault.name == "corp-vault"
    assert vault.kind == "bitwarden-secrets"
    assert vault.endpoint == "https://vault.nyssa.example"
    assert vault.credential == "oauth://github/pat"
    assert vault.cache_ttl_seconds == 120
    assert vault.settings == {"organizationId": "org-1"}
    assert vault.secrets["accessToken"].value == sealed_tok
    assert vault.secrets["accessToken"].encrypted is True
    assert vault.secrets["accessToken"].provider == "aesLocal"

    app_res = ConfigurationManifestReader.read_application(manifest_path, "nyssa")
    assert app_res.ok
    assert app_res.value.lock_timeout_ms == 12500
    assert Path(app_res.value.configuration_directory) == (tmp_path / "config-store").resolve()


def test_manifest_reader_rejects_legacy_per_application_key_vault(tmp_path: Path) -> None:
    """Verify legacy applications.<app>.keyVault is rejected fail-closed with Validation."""
    manifest_path = tmp_path / "config-manifest.json"
    legacy = {
        "version": 1,
        "applications": {
            "nyssa": {
                "configurationDirectory": "./config-store",
                "keyVault": {
                    "name": "corp-vault",
                    "kind": "bitwarden-secrets",
                },
            },
        },
    }
    assert ProtectedFileSystem.create_file(manifest_path, json.dumps(legacy)).ok

    res = ConfigurationManifestReader.read_manifest(manifest_path)
    assert not res.ok
    assert res.category == "Validation"
    assert res.message == "manifest: applications.nyssa.keyVault is no longer supported; move vault definitions to root 'keyVaults'"


@pytest.mark.skipif(sys.platform == "win32", reason="POSIX mode 0644 test")
def test_manifest_0644_with_secrets_fails_forbidden_first_without_leaking_secret(tmp_path: Path) -> None:
    """Verify 0644 manifests with root or legacy secrets fail Forbidden (chmod 600) BEFORE envelope or legacy validation."""
    canary = "CANARY-LEAKED-PLAINTEXT-TOKEN-999"

    # 1. Root keyVaults with plaintext secret on a 0644 file -> Forbidden FIRST
    root_manifest = tmp_path / "root-0644.json"
    root_payload = {
        "version": 1,
        "keyVaults": {
            "bws-prod": {
                "kind": "bitwarden-secrets",
                "secrets": {"accessToken": canary},
            },
        },
        "applications": {},
    }
    root_manifest.write_text(json.dumps(root_payload), encoding="utf-8")
    root_manifest.chmod(0o644)

    res_root = ConfigurationManifestReader.read_manifest(root_manifest)
    assert not res_root.ok
    assert res_root.category == "Forbidden"
    assert "chmod 600" in res_root.message
    assert canary not in res_root.message

    # 2. Legacy applications.<app>.keyVault.secrets on a 0644 file -> Forbidden FIRST
    legacy_manifest = tmp_path / "legacy-0644.json"
    legacy_payload = {
        "version": 1,
        "applications": {
            "orders": {
                "configurationDirectory": "./store",
                "keyVault": {
                    "name": "bws",
                    "kind": "bitwarden-secrets",
                    "secrets": {"accessToken": canary},
                },
            },
        },
    }
    legacy_manifest.write_text(json.dumps(legacy_payload), encoding="utf-8")
    legacy_manifest.chmod(0o644)

    res_legacy = ConfigurationManifestReader.read_manifest(legacy_manifest)
    assert not res_legacy.ok
    assert res_legacy.category == "Forbidden"
    assert "chmod 600" in res_legacy.message
    assert canary not in res_legacy.message


def test_manifest_reader_rejects_invalid_vault_names_fields_and_unencrypted_secrets(tmp_path: Path) -> None:
    """Verify validation of vault names, inner name mismatch, URLs, credentials, and unencrypted/malformed envelopes."""
    canary = "CANARY-PLAINTEXT-BWS-SECRET-777"
    manifest_path = tmp_path / "config-manifest.json"

    v1_payload = base64.urlsafe_b64encode(bytes([0x01]) + b"\x00" * 32).rstrip(b"=").decode("ascii")
    short_v2 = base64.urlsafe_b64encode(bytes([0x02]) + b"\x00" * 10).rstrip(b"=").decode("ascii")

    invalid_secret_cases: list[object] = [
        canary,
        {"value": canary, "encrypted": False, "provider": "aesLocal"},
        {"value": v1_payload, "encrypted": True, "provider": "aesLocal"},
        {"value": short_v2, "encrypted": True, "provider": "aesLocal"},
        {"value": "not+url+safe==", "encrypted": True, "provider": "aesLocal"},
        {"value": short_v2 + "AAAAAAAAAAAAAAAAAAAAAAAAAAAA", "encrypted": True, "provider": "literal"},
    ]
    for bad_secret in invalid_secret_cases:
        doc = {
            "version": 1,
            "keyVaults": {
                "bws-prod": {
                    "kind": "bitwarden-secrets",
                    "secrets": {"accessToken": bad_secret},
                },
            },
            "applications": {},
        }
        assert ProtectedFileSystem.create_file(manifest_path, json.dumps(doc)).ok
        res = ConfigurationManifestReader.read_manifest(manifest_path)
        assert not res.ok
        assert res.category == "Validation"
        assert (
            res.message
            == "manifest: keyVaults.bws-prod.secrets.accessToken must be an encrypted aesLocal envelope; plaintext secrets are forbidden in config-manifest.json"
        )
        assert canary not in res.message

    # Reserved or invalid vault names
    for bad_name in ("aesLocal", "literal", "oauth", "bad/name", " space", ""):
        name_res = ConfigurationManifestReader.validate_vault_name(bad_name)
        assert not name_res.ok and name_res.category == "Validation"

    # Mismatched inner 'name', relative endpoint, invalid credential
    for bad_vault in (
        {"name": "other-name", "kind": "bitwarden-secrets"},
        {"kind": "bitwarden-secrets", "endpoint": "/relative/path"},
        {"kind": "bitwarden-secrets", "credential": "../escape/token"},
        {"kind": "bitwarden-secrets", "cacheTTLSeconds": True},
        {"kind": "bitwarden-secrets", "settings": {"cacheTTLSeconds": "-5"}},
    ):
        doc = {"version": 1, "keyVaults": {"bws-prod": bad_vault}, "applications": {}}
        assert ProtectedFileSystem.create_file(manifest_path, json.dumps(doc)).ok
        res = ConfigurationManifestReader.read_manifest(manifest_path)
        assert not res.ok and res.category == "Validation"


def test_manifest_writer_set_vault_preserve_secrets_remove_vault_and_compact_json(tmp_path: Path) -> None:
    """Verify set_vault seals secrets, preserve_existing_secrets retains envelopes without re-sealing, and remove_vault omits empty keyVaults."""
    manifest_path = tmp_path / "config-manifest.json"
    keyring = KeyringInMemory()
    writer = ConfigurationManifestWriter()
    canary = "CANARY-BWS-ACCESS-TOKEN-12345"

    app_res = writer.provision_application(
        manifest_path=manifest_path,
        application_name="otherApp",
        configuration_directory=tmp_path / "other-store",
        lock_timeout_ms=5000,
    )
    assert app_res.ok

    # Minimal manifest omits empty keyVaults
    raw_initial = json.loads(manifest_path.read_text(encoding="utf-8"))
    assert "keyVaults" not in raw_initial

    set_res = writer.set_vault(
        manifest_path=manifest_path,
        vault=VaultOptions(
            name="bws-prod",
            kind="bitwarden-secrets",
            cache_ttl_seconds=90,
            settings={"projectId": "proj-prod-1"},
            secrets={"accessToken": canary},
        ),
        keyring=keyring,
    )
    assert set_res.ok
    original_ciphertext = set_res.value.secrets["accessToken"].value
    assert canary not in manifest_path.read_text(encoding="utf-8")

    raw_with_vault = json.loads(manifest_path.read_text(encoding="utf-8"))
    assert "name" not in raw_with_vault["keyVaults"]["bws-prod"]
    assert raw_with_vault["keyVaults"]["bws-prod"]["secrets"]["accessToken"] == {
        "value": original_ciphertext,
        "provider": "aesLocal",
        "encrypted": True,
    }
    assert "otherApp" in raw_with_vault["applications"]

    # Update settings with preserve_existing_secrets=True and NO keyring needed when secrets={}
    failing_keyring = KeyringInMemory(fail_on_read=True, fail_on_write=True)
    preserved_res = writer.set_vault(
        manifest_path=manifest_path,
        vault=VaultOptions(
            name="bws-prod",
            kind="bitwarden-secrets",
            cache_ttl_seconds=30,
            settings={"projectId": "proj-prod-2"},
            secrets={},
        ),
        keyring=failing_keyring,
        preserve_existing_secrets=True,
    )
    assert preserved_res.ok
    assert preserved_res.value.cache_ttl_seconds == 30
    assert preserved_res.value.settings == {"projectId": "proj-prod-2"}
    # Exact same ciphertext envelope preserved without decrypting or re-encrypting!
    assert preserved_res.value.secrets["accessToken"].value == original_ciphertext

    # Verify manifest key is stored under _manifest and unseals with AAD ("keyVaults/bws-prod", "accessToken")
    mkey = keyring.get_key("nyssa-ai-configuration", MANIFEST_KEY_ACCOUNT)
    assert mkey.ok and mkey.value is not None
    cipher_check = ConfigurationCipherAesGcm.create(mkey.value)
    assert cipher_check.ok
    opened = cipher_check.value.open(original_ciphertext, "keyVaults/bws-prod", "accessToken")
    assert opened.ok and opened.value == canary

    # Provisioning another app preserves root keyVaults
    assert writer.provision_application(manifest_path, "nyssa", tmp_path / "nyssa-store", 7500).ok
    doc_after_prov = ConfigurationManifestReader.read_manifest(manifest_path)
    assert doc_after_prov.ok
    assert "bws-prod" in doc_after_prov.value.key_vaults
    assert "otherApp" in doc_after_prov.value.applications
    assert doc_after_prov.value.applications["nyssa"].lock_timeout_ms == 7500

    # Remove vault -> returns True once, False when already absent, and omits empty keyVaults on disk
    rem_1 = writer.remove_vault(manifest_path, "bws-prod")
    assert rem_1.ok and rem_1.value is True
    rem_2 = writer.remove_vault(manifest_path, "bws-prod")
    assert rem_2.ok and rem_2.value is False

    raw_after_remove = json.loads(manifest_path.read_text(encoding="utf-8"))
    assert "keyVaults" not in raw_after_remove
    assert "nyssa" in raw_after_remove["applications"]


def test_manifest_repr_redacts_secret_and_envelope_values() -> None:
    """Verify repr() on ManifestSecretEnvelope, VaultManifestEntry, and VaultOptions redacts secret payloads."""
    canary_cipher = "AgCANARYCIPHERTEXTPAYLOAD12345678901234567890"
    canary_plain = "CANARY-PLAIN-SECRET-VALUE"

    env = ManifestSecretEnvelope(value=canary_cipher)
    entry = VaultManifestEntry(name="bws-prod", kind="bitwarden-secrets", secrets={"accessToken": env})
    opts = VaultOptions(name="bws-prod", kind="bitwarden-secrets", secrets={"accessToken": canary_plain})

    assert canary_cipher not in repr(env)
    assert canary_cipher not in repr(entry)
    assert canary_plain not in repr(opts)
