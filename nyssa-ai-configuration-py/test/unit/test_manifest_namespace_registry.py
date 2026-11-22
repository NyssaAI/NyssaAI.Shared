# Responsibility: Verify shared namespace manifest registration, application opt-ins, and writer preservation.
# Public interface:
# +-- test_manifest_reader_resolves_opted_in_namespace_files_and_rejects_unknown_names(tmp_path)
# `-- test_manifest_writer_preserves_namespace_registry_and_application_opt_ins(tmp_path)
"""Unit tests for shared namespace manifest routing."""

import json
from pathlib import Path

from nyssa_ai_configuration import (
    ConfigurationManifestReader,
    ConfigurationManifestWriter,
    ProtectedFileSystem,
    VaultOptions,
)


def test_manifest_reader_resolves_opted_in_namespace_files_and_rejects_unknown_names(tmp_path: Path) -> None:
    manifest_path = tmp_path / "config-manifest.json"
    payload = {
        "version": 1,
        "namespaceRegistry": {"crm": "./shared/crm.json", "private": "./shared/private.json"},
        "applications": {"tool": {"configurationDirectory": "./tool", "sharedNamespaces": ["crm"]}},
    }
    assert ProtectedFileSystem.create_file(manifest_path, json.dumps(payload)).ok

    read = ConfigurationManifestReader.read_application(manifest_path, "tool")

    assert read.ok
    assert read.value.namespace_files == {"crm": str((tmp_path / "shared" / "crm.json").resolve())}

    payload["applications"]["tool"]["sharedNamespaces"] = ["missing"]
    assert ProtectedFileSystem.create_file(manifest_path, json.dumps(payload)).ok
    invalid = ConfigurationManifestReader.read_manifest(manifest_path)
    assert not invalid.ok
    assert invalid.category == "Validation"
    assert "unknown namespaceRegistry entry 'missing'" in invalid.message


def test_manifest_writer_preserves_namespace_registry_and_application_opt_ins(tmp_path: Path) -> None:
    manifest_path = tmp_path / "config-manifest.json"
    payload = {
        "version": 1,
        "namespaceRegistry": {"crm": "./shared/crm.json"},
        "applications": {"tool": {"configurationDirectory": "./old-tool", "sharedNamespaces": ["crm"]}},
    }
    assert ProtectedFileSystem.create_file(manifest_path, json.dumps(payload)).ok
    writer = ConfigurationManifestWriter()

    provisioned = writer.provision_application(manifest_path, "tool", tmp_path / "new-tool")
    assert provisioned.ok
    vaulted = writer.set_vault(manifest_path, VaultOptions(name="test-vault", kind="test"))
    assert vaulted.ok

    saved = json.loads(manifest_path.read_text(encoding="utf-8"))
    assert saved["namespaceRegistry"] == {"crm": "./shared/crm.json"}
    assert saved["applications"]["tool"]["sharedNamespaces"] == ["crm"]
