# Responsibility: Provision application sections and manage encrypted root keyVaults in config-manifest.json under lock.
# Public interface:
# `-- ConfigurationManifestWriter
#     +-- new(logger, clock)
#     +-- provision_application(manifest_path, application_name, configuration_directory, lock_timeout_ms)
#     +-- remove_vault(manifest_path, vault_name, lock_timeout_ms)
#     `-- set_vault(manifest_path, vault, keyring, lock_timeout_ms, preserve_existing_secrets)
"""Manifest writer preserving sibling sections and sealing root keyVaults secrets with AES-256-GCM."""

from collections.abc import Callable, Mapping
import json
from pathlib import Path
import time
from typing import Final

from nyssa_ai_configuration.encryption.configuration_cipher_aes_gcm import ConfigurationCipherAesGcm
from nyssa_ai_configuration.file_security.directory_safety import DirectorySafety
from nyssa_ai_configuration.file_security.file_lock import FileLock
from nyssa_ai_configuration.file_security.protected_file_system import ProtectedFileSystem
from nyssa_ai_configuration.keyring.keyring import (
    MANIFEST_KEY_ACCOUNT,
    Keyring,
    get_or_create_key,
    resolve_manifest_keyring,
)
from nyssa_ai_configuration.logging.configuration_logger import ConfigurationLogger
from nyssa_ai_configuration.manifest.configuration_manifest_reader import ConfigurationManifestReader
from nyssa_ai_configuration.manifest.types import (
    ApplicationManifestEntry,
    ConfigurationManifestDocument,
    ManifestSecretEnvelope,
    VaultManifestEntry,
    VaultOptions,
)
from nyssa_ai_result import Result, fail, ok

_DEFAULT_LOCK_TIMEOUT_MS: Final[int] = 10000
_SUPPORTED_VERSION: Final[int] = 1


class ConfigurationManifestWriter:
    """Provisions application sections and manages root keyVaults in config-manifest.json under lock."""

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
    ) -> Result[ApplicationManifestEntry]:
        """Provision or update application_name in manifest_path while preserving sibling entries and root keyVaults."""
        started = self._clock()
        if not str(manifest_path).strip():
            return fail("Manifest path cannot be blank.", "Validation")
        if not application_name or not application_name.strip():
            return fail("Application name cannot be blank.", "Validation")
        if not str(configuration_directory).strip():
            return fail("Configuration directory cannot be blank.", "Validation")

        timeout_val = lock_timeout_ms if lock_timeout_ms is not None else _DEFAULT_LOCK_TIMEOUT_MS
        if timeout_val <= 0:
            return fail("lockTimeoutMs must be a positive integer.", "Validation")

        resolved_manifest = Path(str(manifest_path)).resolve()
        prep_res = self._prepare_parent(resolved_manifest)
        if not prep_res.ok:
            return prep_res

        lock = FileLock(resolved_manifest, timeout_ms=timeout_val, logger=self._logger, clock=self._clock)
        acq = lock.acquire()
        if not acq.ok:
            return acq

        try:
            existing_res = self._load_existing(resolved_manifest)
            if not existing_res.ok:
                return existing_res
            existing_doc, apps_dict = existing_res.value

            resolved_config_dir = str(Path(str(configuration_directory)).resolve())
            apps_dict[application_name] = {
                "configurationDirectory": resolved_config_dir,
                "lockTimeoutMs": timeout_val,
            }

            serialized = self._serialize_document(existing_doc.key_vaults, apps_dict)
            write_res = ProtectedFileSystem.create_file(resolved_manifest, serialized)
            if not write_res.ok:
                return write_res

            elapsed_ms = (self._clock() - started) * 1000.0
            if self._logger is not None:
                self._logger.completed("manifest.provision", "success", elapsed_ms, "none", "filesystem", 1)

            return ok(ApplicationManifestEntry(configuration_directory=resolved_config_dir, lock_timeout_ms=timeout_val))
        finally:
            lock.release()

    def set_vault(
        self,
        manifest_path: str | Path,
        vault: VaultOptions,
        keyring: Keyring | None = None,
        lock_timeout_ms: int | None = None,
        preserve_existing_secrets: bool = False,
    ) -> Result[VaultManifestEntry]:
        """Insert or update a root keyVaults.<name> entry, sealing plaintext secrets with the manifest key."""
        started = self._clock()
        if not str(manifest_path).strip():
            return fail("Manifest path cannot be blank.", "Validation")
        timeout_val = lock_timeout_ms if lock_timeout_ms is not None else _DEFAULT_LOCK_TIMEOUT_MS
        if timeout_val <= 0:
            return fail("lockTimeoutMs must be a positive integer.", "Validation")

        valid_res = ConfigurationManifestReader.validate_vault_options(vault)
        if not valid_res.ok:
            return valid_res
        norm_vault = valid_res.value

        resolved_manifest = Path(str(manifest_path)).resolve()
        prep_res = self._prepare_parent(resolved_manifest)
        if not prep_res.ok:
            return prep_res

        lock = FileLock(resolved_manifest, timeout_ms=timeout_val, logger=self._logger, clock=self._clock)
        acq = lock.acquire()
        if not acq.ok:
            return acq

        try:
            existing_res = self._load_existing(resolved_manifest)
            if not existing_res.ok:
                return existing_res
            existing_doc, apps_dict = existing_res.value

            envelopes: dict[str, ManifestSecretEnvelope] = {}
            if preserve_existing_secrets and norm_vault.name in existing_doc.key_vaults:
                for ex_key, ex_env in existing_doc.key_vaults[norm_vault.name].secrets.items():
                    if ex_key not in norm_vault.secrets:
                        envelopes[ex_key] = ex_env

            if norm_vault.secrets:
                sealed_res = self._seal_vault_secrets(resolved_manifest, norm_vault, keyring)
                if not sealed_res.ok:
                    return sealed_res
                envelopes.update(sealed_res.value)

            entry = VaultManifestEntry(
                name=norm_vault.name,
                kind=norm_vault.kind,
                endpoint=norm_vault.endpoint,
                credential=norm_vault.credential,
                settings=dict(norm_vault.settings),
                secrets=envelopes,
                cache_ttl_seconds=norm_vault.cache_ttl_seconds,
            )
            updated_vaults = dict(existing_doc.key_vaults)
            updated_vaults[entry.name] = entry

            serialized = self._serialize_document(updated_vaults, apps_dict)
            write_res = ProtectedFileSystem.create_file(resolved_manifest, serialized)
            if not write_res.ok:
                return write_res

            elapsed_ms = (self._clock() - started) * 1000.0
            if self._logger is not None:
                self._logger.completed("manifest.set_vault", "success", elapsed_ms, entry.name, "filesystem", 1)
            return ok(entry)
        finally:
            lock.release()

    def remove_vault(
        self,
        manifest_path: str | Path,
        vault_name: str,
        lock_timeout_ms: int | None = None,
    ) -> Result[bool]:
        """Remove a root keyVaults.<name> entry under lock, returning Ok(True) if removed or Ok(False) if absent."""
        started = self._clock()
        if not str(manifest_path).strip():
            return fail("Manifest path cannot be blank.", "Validation")
        name_res = ConfigurationManifestReader.validate_vault_name(vault_name)
        if not name_res.ok:
            return name_res
        v_name = name_res.value
        timeout_val = lock_timeout_ms if lock_timeout_ms is not None else _DEFAULT_LOCK_TIMEOUT_MS
        if timeout_val <= 0:
            return fail("lockTimeoutMs must be a positive integer.", "Validation")

        resolved_manifest = Path(str(manifest_path)).resolve()
        if not resolved_manifest.exists():
            return ok(False)

        parent_safe = DirectorySafety.verify(resolved_manifest.parent)
        if not parent_safe.ok:
            return parent_safe

        lock = FileLock(resolved_manifest, timeout_ms=timeout_val, logger=self._logger, clock=self._clock)
        acq = lock.acquire()
        if not acq.ok:
            return acq

        try:
            existing_res = self._load_existing(resolved_manifest)
            if not existing_res.ok:
                return existing_res
            existing_doc, apps_dict = existing_res.value
            if v_name not in existing_doc.key_vaults:
                return ok(False)

            updated_vaults = dict(existing_doc.key_vaults)
            updated_vaults.pop(v_name, None)
            serialized = self._serialize_document(updated_vaults, apps_dict)
            write_res = ProtectedFileSystem.create_file(resolved_manifest, serialized)
            if not write_res.ok:
                return write_res

            elapsed_ms = (self._clock() - started) * 1000.0
            if self._logger is not None:
                self._logger.completed("manifest.remove_vault", "success", elapsed_ms, v_name, "filesystem", 1)
            return ok(True)
        finally:
            lock.release()

    @staticmethod
    def _prepare_parent(resolved_manifest: Path) -> Result[Path]:
        parent_safe = DirectorySafety.verify(resolved_manifest.parent)
        if not parent_safe.ok:
            return parent_safe
        return ProtectedFileSystem.create_directory(resolved_manifest.parent)

    @staticmethod
    def _load_existing(
        resolved_manifest: Path,
    ) -> Result[tuple[ConfigurationManifestDocument, dict[str, object]]]:
        if not resolved_manifest.exists():
            return ok((ConfigurationManifestDocument(version=_SUPPORTED_VERSION, applications={}, key_vaults={}), {}))
        doc_res = ConfigurationManifestReader.read_manifest(resolved_manifest)
        if not doc_res.ok:
            return doc_res
        raw = resolved_manifest.read_text(encoding="utf-8")
        loaded: object = json.loads(ConfigurationManifestReader.strip_jsonc(raw))
        raw_apps = loaded.get("applications") if isinstance(loaded, dict) else None
        apps_dict: dict[str, object] = dict(raw_apps) if isinstance(raw_apps, dict) else {}
        return ok((doc_res.value, apps_dict))

    @staticmethod
    def _seal_vault_secrets(
        resolved_manifest: Path,
        vault: VaultOptions,
        keyring: Keyring | None,
    ) -> Result[dict[str, ManifestSecretEnvelope]]:
        manifest_kr = resolve_manifest_keyring(resolved_manifest, keyring)
        key_res = get_or_create_key(MANIFEST_KEY_ACCOUNT, manifest_kr)
        if not key_res.ok:
            return key_res
        key_buf = bytearray(key_res.value)
        try:
            cipher_res = ConfigurationCipherAesGcm.create(bytes(key_buf))
            if not cipher_res.ok:
                return cipher_res
            cipher = cipher_res.value
            aad_ns = f"keyVaults/{vault.name}"
            sealed_map: dict[str, ManifestSecretEnvelope] = {}
            for sk, sv in vault.secrets.items():
                sealed = cipher.seal(sv, aad_ns, sk)
                if not sealed.ok:
                    return sealed
                sealed_map[sk] = ManifestSecretEnvelope(value=sealed.value, encrypted=True, provider="aesLocal")
            return ok(sealed_map)
        finally:
            for idx in range(len(key_buf)):
                key_buf[idx] = 0

    @staticmethod
    def _serialize_document(
        key_vaults: Mapping[str, VaultManifestEntry],
        applications: Mapping[str, object],
    ) -> str:
        doc: dict[str, object] = {"version": _SUPPORTED_VERSION}
        if key_vaults:
            doc["keyVaults"] = {
                name: ConfigurationManifestWriter._serialize_vault_entry(entry)
                for name, entry in key_vaults.items()
            }
        doc["applications"] = dict(applications)
        return json.dumps(doc, indent=2) + "\n"

    @staticmethod
    def _serialize_vault_entry(entry: VaultManifestEntry) -> dict[str, object]:
        out: dict[str, object] = {"kind": entry.kind}
        if entry.endpoint is not None:
            out["endpoint"] = entry.endpoint
        if entry.credential is not None:
            out["credential"] = entry.credential
        if entry.cache_ttl_seconds is not None:
            out["cacheTTLSeconds"] = entry.cache_ttl_seconds
        if entry.settings:
            out["settings"] = dict(entry.settings)
        if entry.secrets:
            out["secrets"] = {
                sk: {"value": env.value, "provider": env.provider, "encrypted": True}
                for sk, env in entry.secrets.items()
            }
        return out
