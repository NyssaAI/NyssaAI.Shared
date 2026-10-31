# Responsibility: Bootstrap configuration from manifest/keyring and implement the public Configuration client facade.
# Public interface:
# `-- ConfigurationClient
#     +-- authorize(namespace, key, options, browser_launcher)
#     +-- authorize_provider(provider_name, browser_launcher)
#     +-- create(options, keyring, vault_factories, oauth_flow_client, browser_launcher, logger, clock)
#     +-- get(namespace, key)
#     +-- get_as(namespace, key, target_type)
#     +-- get_namespace(namespace, target_type)
#     +-- list_keys(namespace)
#     +-- merge_settings(namespace, value)
#     +-- move(namespace, key, destination_provider)
#     +-- new(application_name, configuration_directory, lock_timeout_ms, store, vault_auth)
#     +-- reauthorize(namespace, key, browser_launcher)
#     +-- recover(handle)
#     +-- remove(namespace, key)
#     +-- set(namespace, key, value, provider)
#     +-- set_as(namespace, key, value, provider)
#     +-- set_document_write_interceptor(interceptor)
#     `-- set_namespace(namespace, value)
"""Bootstrap resolver and public ConfigurationClient facade."""

from collections.abc import Callable, Mapping, Sequence
import dataclasses
import json
from pathlib import Path
import time

from nyssa_ai_configuration.cloud_key_vaults.cloud_key_vault import CloudKeyVault
from nyssa_ai_configuration.cloud_key_vaults.cloud_key_vault_factory import (
    CloudKeyVaultFactory,
)
from nyssa_ai_configuration.encryption.configuration_cipher_aes_gcm import (
    ConfigurationCipherAesGcm,
)
from nyssa_ai_configuration.file_security.directory_safety import DirectorySafety
from nyssa_ai_configuration.file_security.protected_file_system import ProtectedFileSystem
from nyssa_ai_configuration.keyring.keyring import Keyring, get_or_create_key
from nyssa_ai_configuration.keyring.keyring_default import KeyringDefault
from nyssa_ai_configuration.logging.configuration_logger import ConfigurationLogger
from nyssa_ai_configuration.manifest.configuration_manifest_reader import (
    ConfigurationManifestReader,
)
from nyssa_ai_configuration.manifest.configuration_manifest_writer import (
    ConfigurationManifestWriter,
)
from nyssa_ai_configuration.manifest.types import BootstrapOptions, VaultOptions
from nyssa_ai_configuration.oauth.browser_launcher import BrowserLauncher
from nyssa_ai_configuration.oauth.oauth_flow_client import OauthFlowClient
from nyssa_ai_configuration.oauth.oauth_token_provider import OauthTokenProvider
from nyssa_ai_configuration.oauth.types import OAuthOptions
from nyssa_ai_configuration.recovery.configuration_recovery_handle import (
    ConfigurationRecoveryHandle,
)
from nyssa_ai_configuration.recovery.configuration_recovery_token_protector import (
    ConfigurationRecoveryTokenProtector,
)
from nyssa_ai_configuration.recovery.types import (
    ConfigurationAttempt,
    ConfigurationMove,
    ConfigurationRecoveryOutcome,
)
from nyssa_ai_configuration.results.result import Result, fail, ok
from nyssa_ai_configuration.values.configuration_store import ConfigurationStore
from nyssa_ai_configuration.values.types import ConfigurationRemoval


class ConfigurationClient:
    """High-level configuration client implementing F-001 through F-204."""

    def __init__(
        self,
        application_name: str,
        configuration_directory: str,
        lock_timeout_ms: int,
        store: ConfigurationStore,
        vault_auth: Mapping[str, tuple[str, str, OAuthOptions | None]] | None = None,
    ) -> None:
        """Initialize with resolved bootstrap metadata, underlying store, and vault OAuth bindings."""
        self.application_name = application_name
        self.configuration_directory = configuration_directory
        self.lock_timeout_ms = lock_timeout_ms
        self._store = store
        self._vault_auth: dict[str, tuple[str, str, OAuthOptions | None]] = (
            dict(vault_auth) if vault_auth is not None else {}
        )

    @classmethod
    def create(
        cls,
        options: BootstrapOptions,
        keyring: Keyring | None = None,
        vault_factories: Sequence[CloudKeyVaultFactory] = (),
        oauth_flow_client: OauthFlowClient | None = None,
        browser_launcher: BrowserLauncher | None = None,
        logger: ConfigurationLogger | None = None,
        clock: Callable[[], float] = time.monotonic,
    ) -> Result["ConfigurationClient"]:
        """Resolve manifest, verify directory safety, unlock/create master key, wire vaults, and return ConfigurationClient."""
        app_name_res = DirectorySafety.validate_segment(options.application_name, "application_name")
        if not app_name_res.ok:
            return app_name_res
        app_name = app_name_res.value

        manifest_path = (
            Path(options.manifest_path).resolve()
            if options.manifest_path and options.manifest_path.strip()
            else ConfigurationManifestReader.default_manifest_path(options.home_directory)
        )

        entry_res = ConfigurationManifestReader.read_application(manifest_path, app_name)
        if not entry_res.ok and entry_res.category != "NotFound":
            return entry_res

        existing = entry_res.value if entry_res.ok else None
        if existing is not None:
            config_dir = existing.configuration_directory
            timeout_ms = (
                options.lock_timeout_ms
                if options.lock_timeout_ms is not None
                else existing.lock_timeout_ms
            )
            if timeout_ms <= 0:
                return fail("lockTimeoutMs must be a positive integer.", "Validation")
            vault_opt: VaultOptions | None = (
                existing.key_vault if existing.key_vault is not None else options.key_vault
            )
        else:
            config_dir = (
                str(Path(options.configuration_directory).resolve())
                if options.configuration_directory and options.configuration_directory.strip()
                else str((manifest_path.parent / app_name).resolve())
            )
            timeout_ms = options.lock_timeout_ms if options.lock_timeout_ms is not None else 10000
            if timeout_ms <= 0:
                return fail("lockTimeoutMs must be a positive integer.", "Validation")
            vault_opt = options.key_vault
            writer = ConfigurationManifestWriter(logger=logger, clock=clock)
            prov_res = writer.provision_application(manifest_path, app_name, config_dir, timeout_ms)
            if not prov_res.ok:
                return prov_res

        dir_safe = DirectorySafety.verify(config_dir)
        if not dir_safe.ok:
            return dir_safe
        dir_created = ProtectedFileSystem.create_directory(config_dir)
        if not dir_created.ok:
            return dir_created

        active_keyring: Keyring = keyring if keyring is not None else KeyringDefault()
        key_res = get_or_create_key(app_name, active_keyring)
        if not key_res.ok:
            return key_res
        master_key = key_res.value

        cipher_res = ConfigurationCipherAesGcm.create(master_key)
        if not cipher_res.ok:
            return cipher_res
        cipher = cipher_res.value

        protector = ConfigurationRecoveryTokenProtector(master_key, app_name, config_dir)
        oauth_provider = OauthTokenProvider(cipher, oauth_flow_client, browser_launcher, logger=logger, clock=clock)

        store_box: list[ConfigurationStore] = []
        vaults: dict[str, CloudKeyVault] = {}
        credentials: dict[str, tuple[str, str]] = {}
        vault_auth: dict[str, tuple[str, str, OAuthOptions | None]] = {}

        class _StoreTokenSource:
            def __init__(self, vault_name: str) -> None:
                self._vault_name = vault_name

            def get_access_token(self) -> Result[str]:
                return store_box[0].resolve_vault_credential(self._vault_name)

        if vault_opt is not None:
            factory = next((f for f in vault_factories if f.kind == vault_opt.kind), None)
            if factory is None:
                return fail(f"No CloudKeyVaultFactory registered for kind '{vault_opt.kind}'.", "Validation")
            tok_src: _StoreTokenSource | None = None
            cred_ns, cred_key = "", ""
            if vault_opt.credential is not None:
                parts = vault_opt.credential.removeprefix("oauth://").split("/", 1)
                if len(parts) != 2 or not parts[0].strip() or not parts[1].strip():
                    return fail(f"Invalid vault credential address '{vault_opt.credential}'.", "Validation")
                cred_ns, cred_key = parts[0].strip(), parts[1].strip()
                credentials[vault_opt.name] = (cred_ns, cred_key)
                tok_src = _StoreTokenSource(vault_opt.name)

            created_vault = factory.create(vault_opt, tok_src)
            if not created_vault.ok:
                return created_vault
            vaults[vault_opt.name] = created_vault.value

            auth_opts_res = factory.authorization(vault_opt)
            if not auth_opts_res.ok:
                return auth_opts_res
            if cred_ns and cred_key:
                vault_auth[vault_opt.name] = (cred_ns, cred_key, auth_opts_res.value)

        store = ConfigurationStore(
            config_dir, timeout_ms, cipher, vaults, credentials, oauth_provider, protector, logger, clock
        )
        store_box.append(store)
        return ok(cls(app_name, config_dir, timeout_ms, store, vault_auth))

    def set_document_write_interceptor(self, interceptor: Callable[[str], Result[None]] | None = None) -> None:
        """Configure a write interceptor on the underlying store for recovery and OAuth tests."""
        self._store.set_document_write_interceptor(interceptor)

    def get(self, namespace: str, key: str) -> Result[str]:
        """Read a string setting or secret at (namespace, key)."""
        return self._store.get(namespace, key)

    def set(self, namespace: str, key: str, value: str, provider: str | None = None) -> Result[None]:
        """Write a string setting or secret at (namespace, key)."""
        return self._store.set(namespace, key, value, provider=provider, is_json=False)

    def get_as[T](self, namespace: str, key: str, target_type: Callable[[object], T] | type[T]) -> Result[T]:
        """Read and deserialize a typed DTO or primitive at (namespace, key)."""
        entry_res = self._store.get_entry(namespace, key)
        if not entry_res.ok:
            return entry_res
        raw_res = self._store.get(namespace, key)
        if not raw_res.ok:
            return raw_res
        entry = entry_res.value
        raw_str = raw_res.value

        if target_type is str:
            if not entry.is_json_value:
                return ok(raw_str)  # type: ignore[arg-type]
            try:
                parsed_str: object = json.loads(raw_str)
            except Exception as exc:
                return fail(f"Stored JSON string at '{namespace}/{key}' is invalid: {exc}", "Validation", exc)
            if not isinstance(parsed_str, str):
                return fail(f"Stored JSON value at '{namespace}/{key}' is not a string.", "Validation")
            return ok(parsed_str)  # type: ignore[arg-type]

        try:
            parsed: object = json.loads(raw_str)
        except Exception as exc:
            return fail(f"Stored value at '{namespace}/{key}' is not valid JSON: {exc}", "Validation", exc)
        return self._convert_to_type(parsed, target_type)

    def set_as(self, namespace: str, key: str, value: object, provider: str | None = None) -> Result[None]:
        """Serialize and write a typed DTO or primitive at (namespace, key)."""
        try:
            serialized = json.dumps(self._to_jsonable(value), separators=(",", ":"))
        except Exception as exc:
            return fail(f"Failed to serialize value for '{namespace}/{key}': {exc}", "Validation", exc)
        return self._store.set(namespace, key, serialized, provider=provider, is_json=True)

    def get_namespace[T](self, namespace: str, target_type: Callable[[object], T] | type[T] = dict) -> Result[T]:  # type: ignore[assignment]
        """Assemble all entries in namespace and deserialize into target_type."""
        raw_res = self._store.get_namespace(namespace)
        if not raw_res.ok:
            return raw_res
        try:
            parsed: object = json.loads(raw_res.value)
        except Exception as exc:
            return fail(f"Namespace '{namespace}' could not be parsed as JSON: {exc}", "Validation", exc)
        return self._convert_to_type(parsed, target_type)

    def set_namespace(self, namespace: str, value: object) -> ConfigurationAttempt[None]:
        """Decompose value into top-level namespace entries, replacing omitted local keys."""
        try:
            serialized = json.dumps(self._to_jsonable(value), separators=(",", ":"))
        except Exception as exc:
            return ConfigurationAttempt(result=fail(f"Failed to serialize namespace '{namespace}': {exc}", "Validation", exc))
        return self._store.set_namespace(namespace, serialized)

    def merge_settings(self, namespace: str, value: object) -> ConfigurationAttempt[None]:
        """Decompose value and shallow-merge supplied top-level keys into namespace."""
        try:
            serialized = json.dumps(self._to_jsonable(value), separators=(",", ":"))
        except Exception as exc:
            return ConfigurationAttempt(result=fail(f"Failed to serialize namespace '{namespace}': {exc}", "Validation", exc))
        return self._store.merge_settings(namespace, serialized)

    def list_keys(self, namespace: str) -> Result[list[str]]:
        """List all keys in namespace in UTF-16 code-unit ordinal order."""
        return self._store.list_keys(namespace)

    def remove(self, namespace: str, key: str) -> Result[ConfigurationRemoval]:
        """Remove (namespace, key) from its provider and local namespace file."""
        return self._store.remove(namespace, key)

    def move(self, namespace: str, key: str, destination_provider: str) -> ConfigurationAttempt[ConfigurationMove]:
        """Migrate (namespace, key) to destination_provider."""
        return self._store.move(namespace, key, destination_provider)

    def authorize(self, namespace: str, key: str, options: OAuthOptions, browser_launcher: BrowserLauncher | None = None) -> Result[None]:
        """Run interactive OAuth 2.0 PKCE sign-in and store the grant at (namespace, key)."""
        return self._store.authorize(namespace, key, options, browser_launcher)

    def authorize_provider(self, provider_name: str, browser_launcher: BrowserLauncher | None = None) -> Result[None]:
        """Run interactive OAuth 2.0 PKCE sign-in for a configured cloud key vault's credential."""
        info = self._vault_auth.get(provider_name)
        if info is None:
            return fail(f"Provider '{provider_name}' is not configured with an OAuth credential.", "Validation")
        ns, key, opts = info
        if opts is None:
            return fail(f"Provider '{provider_name}' does not supply OAuth authorization options.", "Validation")
        return self._store.authorize(ns, key, opts, browser_launcher)

    def reauthorize(self, namespace: str, key: str, browser_launcher: BrowserLauncher | None = None) -> Result[None]:
        """Re-run interactive OAuth 2.0 PKCE sign-in using endpoint metadata stored at (namespace, key)."""
        return self._store.reauthorize(namespace, key, browser_launcher)

    def recover(self, handle: ConfigurationRecoveryHandle) -> Result[ConfigurationRecoveryOutcome]:
        """Resume and reconcile an interrupted multi-step configuration operation."""
        return self._store.recover(handle)

    @staticmethod
    def _to_jsonable(value: object) -> object:
        if dataclasses.is_dataclass(value) and not isinstance(value, type):
            return dataclasses.asdict(value)
        return value

    @staticmethod
    def _convert_to_type[T](parsed: object, target_type: Callable[[object], T] | type[T]) -> Result[T]:
        if target_type in (dict, object):
            return ok(parsed)  # type: ignore[arg-type]
        if isinstance(target_type, type) and dataclasses.is_dataclass(target_type):
            if not isinstance(parsed, dict):
                return fail(f"Expected JSON object to deserialize into {target_type.__name__}.", "Validation")
            try:
                return ok(target_type(**parsed))  # type: ignore[arg-type]
            except Exception as exc:
                return fail(f"Failed to bind JSON object to {target_type.__name__}: {exc}", "Validation", exc)
        if isinstance(target_type, type) and isinstance(parsed, target_type):
            return ok(parsed)  # type: ignore[arg-type]
        try:
            return ok(target_type(parsed))
        except Exception as exc:
            return fail(f"Failed to convert JSON value to target type: {exc}", "Validation", exc)
