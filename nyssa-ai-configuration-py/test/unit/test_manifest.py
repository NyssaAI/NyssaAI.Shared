# Responsibility: Verify ConfigurationManifestReader and ConfigurationManifestWriter JSONC parsing, TimeSpan compatibility, and sibling preservation.
# Public interface:
# +-- test_manifest_reader_parses_jsonc_and_legacy_dotnet_timespan(tmp_path)
# `-- test_manifest_writer_preserves_sibling_applications_and_writes_lock_timeout_ms(tmp_path)
"""Unit tests for ConfigurationManifestReader and ConfigurationManifestWriter."""

import json
from pathlib import Path

from nyssa_ai_configuration.file_security.protected_file_system import ProtectedFileSystem
from nyssa_ai_configuration.manifest.configuration_manifest_reader import (
    ConfigurationManifestReader,
)
from nyssa_ai_configuration.manifest.configuration_manifest_writer import (
    ConfigurationManifestWriter,
)
from nyssa_ai_configuration.manifest.types import VaultOptions


def test_manifest_reader_parses_jsonc_and_legacy_dotnet_timespan(tmp_path: Path) -> None:
    """Verify ConfigurationManifestReader strips JSONC comments/trailing commas and parses legacy lockTimeout."""
    manifest_path = tmp_path / "config-manifest.json"
    manifest_text = """
    {
      // Application configuration root with comments
      "version": 1,
      "applications": {
        "nyssa": {
          "configurationDirectory": "./config-store",
          "lockTimeout": "00:00:12.500", /* Legacy .NET TimeSpan */
          "keyVault": {
            "name": "corp-vault",
            "kind": "bitwarden",
            "credential": "oauth://github/pat",
          },
        },
      },
    }
    """
    created = ProtectedFileSystem.create_file(manifest_path, manifest_text)
    assert created.ok

    result = ConfigurationManifestReader.read_application(manifest_path, "nyssa")

    assert result.ok
    entry = result.value
    assert entry.lock_timeout_ms == 12500
    assert Path(entry.configuration_directory) == (tmp_path / "config-store").resolve()
    assert entry.key_vault is not None
    assert entry.key_vault.name == "corp-vault"
    assert entry.key_vault.kind == "bitwarden"
    assert entry.key_vault.credential == "oauth://github/pat"


def test_manifest_writer_preserves_sibling_applications_and_writes_lock_timeout_ms(tmp_path: Path) -> None:
    """Verify ConfigurationManifestWriter updates only the named application section and writes lockTimeoutMs."""
    manifest_path = tmp_path / "config-manifest.json"
    writer = ConfigurationManifestWriter()

    other_res = writer.provision_application(
        manifest_path=manifest_path,
        application_name="otherApp",
        configuration_directory=tmp_path / "other-store",
        lock_timeout_ms=5000,
    )
    assert other_res.ok

    vault = VaultOptions(
        name="bitwarden",
        kind="bitwarden",
        secrets={"accessToken": "secret-token"},
    )
    nyssa_res = writer.provision_application(
        manifest_path=manifest_path,
        application_name="nyssa",
        configuration_directory=tmp_path / "nyssa-store",
        lock_timeout_ms=7500,
        key_vault=vault,
    )
    assert nyssa_res.ok

    raw = json.loads(manifest_path.read_text(encoding="utf-8"))
    assert "otherApp" in raw["applications"]
    assert raw["applications"]["nyssa"]["lockTimeoutMs"] == 7500
    assert "lockTimeout" not in raw["applications"]["nyssa"]

    read_back = ConfigurationManifestReader.read_application(manifest_path, "nyssa")
    assert read_back.ok
    assert read_back.value.lock_timeout_ms == 7500
    assert read_back.value.key_vault is not None
    assert read_back.value.key_vault.secrets["accessToken"] == "secret-token"
