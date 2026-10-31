# Responsibility: Provision and update application sections in config-manifest.json under a persistent lock.
# Public interface:
# `-- ConfigurationManifestWriter
#     +-- new(logger, clock)
#     `-- provision_application(manifest_path, application_name, configuration_directory, lock_timeout_ms, key_vault)
"""Manifest writer preserving sibling applications and writing integer lockTimeoutMs."""

from collections.abc import Callable
import json
from pathlib import Path
import time
from typing import Final

from nyssa_ai_configuration.file_security.directory_safety import DirectorySafety
from nyssa_ai_configuration.file_security.file_lock import FileLock
from nyssa_ai_configuration.file_security.protected_file_system import ProtectedFileSystem
from nyssa_ai_configuration.logging.configuration_logger import ConfigurationLogger
from nyssa_ai_configuration.manifest.configuration_manifest_reader import (
    ConfigurationManifestReader,
)
from nyssa_ai_configuration.manifest.types import ApplicationManifestEntry, VaultOptions
from nyssa_ai_configuration.results.result import Result, fail, ok

_DEFAULT_LOCK_TIMEOUT_MS: Final[int] = 10000
_SUPPORTED_VERSION: Final[int] = 1


class ConfigurationManifestWriter:
    """Provisions or updates an application's section in config-manifest.json safely under lock."""

    def __init__(
        self,
        logger: ConfigurationLogger | None = None,
        clock: Callable[[], float] = time.monotonic,
    ) -> None:
        """Initialize writer with optional operational logger and monotonic clock."""
        self._logger = logger
        self._clock = clock

    def provision_application(
        self,
        manifest_path: str | Path,
        application_name: str,
        configuration_directory: str | Path,
        lock_timeout_ms: int | None = None,
        key_vault: VaultOptions | None = None,
    ) -> Result[ApplicationManifestEntry]:
        """Provision or update application_name in manifest_path while preserving sibling entries."""
        started = self._clock()
        if not application_name or not application_name.strip():
            return fail("Application name cannot be blank.", "Validation")
        if not str(configuration_directory).strip():
            return fail("Configuration directory cannot be blank.", "Validation")

        timeout_val = lock_timeout_ms if lock_timeout_ms is not None else _DEFAULT_LOCK_TIMEOUT_MS
        if timeout_val <= 0:
            return fail("lockTimeoutMs must be a positive integer.", "Validation")

        resolved_manifest = Path(str(manifest_path)).resolve()
        parent_safe = DirectorySafety.verify(resolved_manifest.parent)
        if not parent_safe.ok:
            return parent_safe

        dir_res = ProtectedFileSystem.create_directory(resolved_manifest.parent)
        if not dir_res.ok:
            return dir_res

        lock = FileLock(resolved_manifest, timeout_ms=timeout_val, logger=self._logger, clock=self._clock)
        acq = lock.acquire()
        if not acq.ok:
            return acq

        try:
            doc_dict: dict[str, object] = {"version": _SUPPORTED_VERSION, "applications": {}}
            if resolved_manifest.exists():
                file_safe = DirectorySafety.verify_file(resolved_manifest)
                if not file_safe.ok:
                    return file_safe
                try:
                    raw = resolved_manifest.read_text(encoding="utf-8")
                    loaded: object = json.loads(ConfigurationManifestReader.strip_jsonc(raw))
                    if isinstance(loaded, dict):
                        if loaded.get("version") not in (None, _SUPPORTED_VERSION):
                            return fail("Unsupported manifest version.", "Validation")
                        doc_dict = dict(loaded)
                except Exception as exc:
                    return fail(f"Failed to parse existing manifest '{manifest_path}': {exc}", "Validation", exc)

            apps_obj = doc_dict.get("applications")
            apps_dict: dict[str, object] = dict(apps_obj) if isinstance(apps_obj, dict) else {}

            resolved_config_dir = str(Path(str(configuration_directory)).resolve())
            section: dict[str, object] = {
                "configurationDirectory": resolved_config_dir,
                "lockTimeoutMs": timeout_val,
            }
            if key_vault is not None:
                section["keyVault"] = self._serialize_vault(key_vault)

            apps_dict[application_name] = section
            doc_dict["version"] = _SUPPORTED_VERSION
            doc_dict["applications"] = apps_dict

            serialized = json.dumps(doc_dict, indent=2) + "\n"
            write_res = ProtectedFileSystem.create_file(resolved_manifest, serialized)
            if not write_res.ok:
                return write_res

            elapsed_ms = (self._clock() - started) * 1000.0
            if self._logger is not None:
                self._logger.completed("manifest.provision", "success", elapsed_ms, "none", "filesystem", 1)

            return ok(
                ApplicationManifestEntry(
                    configuration_directory=resolved_config_dir,
                    lock_timeout_ms=timeout_val,
                    key_vault=key_vault,
                )
            )
        finally:
            lock.release()

    @staticmethod
    def _serialize_vault(vault: VaultOptions) -> dict[str, object]:
        result: dict[str, object] = {
            "name": vault.name,
            "kind": vault.kind,
        }
        if vault.endpoint is not None:
            result["endpoint"] = vault.endpoint
        if vault.credential is not None:
            result["credential"] = vault.credential
        if vault.settings:
            result["settings"] = dict(vault.settings)
        if vault.secrets:
            result["secrets"] = dict(vault.secrets)
        return result
