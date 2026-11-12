# Responsibility: Bootstrap configuration from manifest/keyring and implement the public Configuration client facade.
# Public interface:
# `-- ConfigurationClient
#     +-- authorize(namespace, key, options, browser_launcher)
#     +-- authorize_provider(provider_name, browser_launcher)
#     +-- capture_secret(namespace, key, options)
#     +-- create(options, keyring, vault_factories, oauth_flow_client, browser_launcher, logger, clock, secret_cache, manifest_keyring)
#     +-- get(namespace, key)
#     +-- get_as(namespace, key, target_type)
#     +-- get_namespace(namespace, target_type)
#     +-- list(namespace)
#     +-- merge_settings(namespace, value)
#     +-- move(namespace, key, destination_provider)
#     +-- new(application_name, configuration_directory, lock_timeout_ms, store, vault_auth, browser_launcher)
#     +-- reauthorize(namespace, key, browser_launcher)
#     +-- recover(handle)
#     +-- remove(namespace, key)
#     +-- set(namespace, key, value, provider)
#     +-- set_as(namespace, key, value, provider)
#     +-- set_document_write_interceptor(interceptor)
#     `-- set_namespace(namespace, value)
"""Public ConfigurationClient facade."""

from __future__ import annotations

from collections.abc import Callable, Mapping, Sequence
import dataclasses
import json
import time

from nyssa_ai_configuration.capture_secret_options import CaptureSecretOptions
from nyssa_ai_configuration.cloud_key_vaults.cloud_key_vault_factory import CloudKeyVaultFactory
from nyssa_ai_configuration.cloud_key_vaults.secret_cache import SecretCache
from nyssa_ai_configuration.configuration_bootstrap import ConfigurationBootstrap
from nyssa_ai_configuration.keyring.keyring import Keyring
from nyssa_ai_configuration.logging.configuration_logger import ConfigurationLogger
from nyssa_ai_configuration.manifest.types import BootstrapOptions
from nyssa_ai_configuration.oauth.browser_launcher import BrowserLauncher
from nyssa_ai_configuration.oauth.oauth_flow_client import OauthFlowClient
from nyssa_ai_configuration.oauth.types import OAuthOptions
from nyssa_ai_configuration.recovery.configuration_recovery_handle import ConfigurationRecoveryHandle
from nyssa_ai_configuration.recovery.types import (
    ConfigurationAttempt,
    ConfigurationMove,
    ConfigurationRecoveryOutcome,
)
from nyssa_ai_configuration.secret_input.adaptive_secret_input_provider import AdaptiveSecretInputProvider
from nyssa_ai_configuration.secret_input.secret_prompt_request import SecretPromptRequest
from nyssa_ai_configuration.secret_receipt import SecretReceipt
from nyssa_ai_configuration.values.configuration_store import ConfigurationStore
from nyssa_ai_configuration.values.types import ConfigurationRemoval
from nyssa_ai_result import Result, fail, ok


class ConfigurationClient:
    """High-level configuration client implementing F-001 through F-204."""

    def __init__(
        self,
        application_name: str,
        configuration_directory: str,
        lock_timeout_ms: int,
        store: ConfigurationStore,
        vault_auth: Mapping[str, tuple[str, str, OAuthOptions | None]] | None = None,
        browser_launcher: BrowserLauncher | None = None,
    ) -> None:
        """Initialize with resolved bootstrap metadata, underlying store, and vault OAuth bindings."""
        self.application_name = application_name
        self.configuration_directory = configuration_directory
        self.lock_timeout_ms = lock_timeout_ms
        self._store = store
        self._vault_auth: dict[str, tuple[str, str, OAuthOptions | None]] = (
            dict(vault_auth) if vault_auth is not None else {}
        )
        self._browser_launcher = browser_launcher

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
        secret_cache: SecretCache | None = None,
        manifest_keyring: Keyring | None = None,
    ) -> Result[ConfigurationClient]:
        """Resolve manifest, verify directory safety, unlock/create master key, wire vaults, and return ConfigurationClient."""
        built = ConfigurationBootstrap.build_store(
            options=options,
            keyring=keyring,
            vault_factories=vault_factories,
            oauth_flow_client=oauth_flow_client,
            browser_launcher=browser_launcher,
            logger=logger,
            clock=clock,
            secret_cache=secret_cache,
            manifest_keyring=manifest_keyring,
        )
        if not built.ok:
            return built
        app_name, config_dir, timeout_ms, store, vault_auth = built.value
        return ok(cls(app_name, config_dir, timeout_ms, store, vault_auth, browser_launcher))

    def set_document_write_interceptor(self, interceptor: Callable[[str], Result[None]] | None = None) -> None:
        """Configure a write interceptor on the underlying store for recovery and OAuth tests."""
        self._store.set_document_write_interceptor(interceptor)

    def capture_secret(
        self, namespace: str, key: str, options: CaptureSecretOptions | None = None
    ) -> Result[SecretReceipt]:
        """Prompt user directly out-of-band for a secret value and store it securely."""
        opts = options or CaptureSecretOptions()
        available = self._store.get_available_secret_providers()
        if opts.forced_provider is not None:
            if opts.forced_provider not in available:
                return fail(f"Forced secret storage provider '{opts.forced_provider}' is not available.", "Validation")
            available = [opts.forced_provider]
        elif opts.allowed_providers is not None:
            available = [p for p in available if p in opts.allowed_providers]
            if not available:
                return fail("No allowed secret storage providers are available.", "Validation")

        preferred = opts.forced_provider or opts.preferred_provider
        if not preferred or preferred not in available:
            preferred = "aesLocal" if "aesLocal" in available else (available[0] if available else "aesLocal")

        prompt_req = SecretPromptRequest(
            namespace,
            key,
            opts.prompt_message or f"Enter secret for '{namespace}/{key}'",
            available,
            preferred,
            opts.confirm,
            opts.timeout_seconds,
        )
        provider = opts.input_provider or AdaptiveSecretInputProvider(opts.mode, self._browser_launcher)
        capture_res = provider.capture(prompt_req)
        if not capture_res.ok:
            return capture_res

        with capture_res.value as captured:
            target_prov, secret_value = captured.selected_provider, captured.value
            set_res = self._store.set(namespace, key, secret_value, provider=target_prov, is_json=False)
            if not set_res.ok:
                return set_res
            return ok(SecretReceipt(namespace, key, target_prov))

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

    def list(self, namespace: str) -> Result[list[str]]:
        """List all keys in namespace in UTF-16 code-unit ordinal order."""
        return self._store.list(namespace)

    def remove(self, namespace: str, key: str) -> Result[ConfigurationRemoval]:
        """Remove (namespace, key) from its provider and local namespace file."""
        return self._store.remove(namespace, key)

    def move(self, namespace: str, key: str, destination_provider: str) -> ConfigurationAttempt[ConfigurationMove]:
        """Migrate (namespace, key) to destination_provider."""
        return self._store.move(namespace, key, destination_provider)

    def authorize(
        self, namespace: str, key: str, options: OAuthOptions, browser_launcher: BrowserLauncher | None = None
    ) -> Result[None]:
        """Run interactive OAuth 2.0 PKCE sign-in and store the grant at (namespace, key)."""
        return self._store.authorize(namespace, key, options, browser_launcher)

    def authorize_provider(self, provider_name: str, browser_launcher: BrowserLauncher | None = None) -> Result[None]:
        """Run interactive OAuth 2.0 PKCE sign-in for a configured cloud key vault's credential."""
        info = self._vault_auth.get(provider_name)
        if info is None or info[2] is None:
            return fail(
                f"Provider '{provider_name}' does not supply OAuth authorization options."
                if info
                else f"Provider '{provider_name}' is not configured with an OAuth credential.",
                "Validation",
            )
        return self._store.authorize(info[0], info[1], info[2], browser_launcher)

    def reauthorize(self, namespace: str, key: str, browser_launcher: BrowserLauncher | None = None) -> Result[None]:
        """Re-run interactive OAuth 2.0 PKCE sign-in using endpoint metadata stored at (namespace, key)."""
        return self._store.reauthorize(namespace, key, browser_launcher)

    def recover(self, handle: ConfigurationRecoveryHandle) -> Result[ConfigurationRecoveryOutcome]:
        """Resume and reconcile an interrupted multi-step configuration operation."""
        return self._store.recover(handle)

    @staticmethod
    def _to_jsonable(value: object) -> object:
        return dataclasses.asdict(value) if dataclasses.is_dataclass(value) and not isinstance(value, type) else value

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
