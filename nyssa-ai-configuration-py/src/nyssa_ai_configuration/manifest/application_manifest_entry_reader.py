# Responsibility: Validate and resolve an individual application's manifest section.
# Public interface:
# `-- ApplicationManifestEntryReader
#     `-- parse(manifest_file, app_name, section, namespace_registry, parse_timeout)
"""Application manifest section parsing."""

from collections.abc import Callable, Mapping
import os
from pathlib import Path

from nyssa_ai_configuration.manifest.namespace_registry import NamespaceRegistry
from nyssa_ai_configuration.manifest.types import ApplicationManifestEntry
from nyssa_ai_result import Result, fail, ok


class ApplicationManifestEntryReader:
    """Parses an application section and resolves its configuration paths."""

    @staticmethod
    def parse(
        manifest_file: Path,
        app_name: str,
        section: dict[object, object],
        namespace_registry: Mapping[str, str],
        parse_timeout: Callable[[str, dict[object, object]], Result[int]],
    ) -> Result[ApplicationManifestEntry]:
        if section.get("keyVault") is not None:
            return fail(
                f"manifest: applications.{app_name}.keyVault is no longer supported; move vault definitions to root 'keyVaults'",
                "Validation",
            )
        config_dir = section.get("configurationDirectory")
        if not isinstance(config_dir, str) or not config_dir.strip():
            return fail(f"manifest: '{app_name}' requires a non-empty 'configurationDirectory'", "Validation")
        timeout_res = parse_timeout(app_name, section)
        if not timeout_res.ok:
            return timeout_res
        files_res = NamespaceRegistry.resolve(
            manifest_file, app_name, section.get("sharedNamespaces"), namespace_registry
        )
        if not files_res.ok:
            return files_res

        resolved_dir = (
            Path(config_dir).resolve()
            if os.path.isabs(config_dir)
            else (manifest_file.parent / config_dir).resolve()
        )
        return ok(ApplicationManifestEntry(
            configuration_directory=str(resolved_dir),
            lock_timeout_ms=timeout_res.value,
            namespace_files=files_res.value,
        ))
