# Responsibility: Coordinate single-key CRUD, provider routing, circular credential checks, moves, OAuth, and recovery.
# Public interface:
# `-- ConfigurationStore
#     +-- authorize(namespace, key, options, browser_launcher)
#     +-- get(namespace, key)
#     +-- get_entry(namespace, key)
#     +-- get_namespace(namespace)
#     +-- list_keys(namespace)
#     +-- merge_settings(namespace, serialized)
#     +-- move(namespace, key, destination_provider)
#     +-- new(configuration_directory, lock_timeout_ms, cipher, vaults, credentials, oauth_provider, recovery_protector, logger, clock)
#     +-- reauthorize(namespace, key, browser_launcher)
#     +-- recover(handle)
#     +-- remove(namespace, key)
#     +-- resolve_vault_credential(vault_name)
#     +-- set(namespace, key, value, provider, is_json)
#     +-- set_document_write_interceptor(interceptor)
#     `-- set_namespace(namespace, serialized)
"""Core configuration store routing local, encrypted, OAuth, and cloud vault entries."""

from collections.abc import Callable, Mapping
from pathlib import Path
import time

from nyssa_ai_configuration.cloud_key_vaults.cloud_key_vault import CloudKeyVault
from nyssa_ai_configuration.cloud_key_vaults.types import ConfigurationPointer
from nyssa_ai_configuration.encryption.configuration_cipher import ConfigurationCipher
from nyssa_ai_configuration.file_security.directory_safety import DirectorySafety
from nyssa_ai_configuration.logging.configuration_logger import ConfigurationLogger
from nyssa_ai_configuration.oauth.browser_launcher import BrowserLauncher
from nyssa_ai_configuration.oauth.oauth_token_provider import OauthTokenProvider
from nyssa_ai_configuration.oauth.types import OAuthOptions
from nyssa_ai_configuration.recovery.configuration_recovery_coordinator import (
    ConfigurationRecoveryCoordinator,
)
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
from nyssa_ai_configuration.values.configuration_document_local_file import (
    ConfigurationDocumentLocalFile,
)
from nyssa_ai_configuration.values.configuration_mover import ConfigurationMover
from nyssa_ai_configuration.values.configuration_value_codec import (
    ConfigurationValueCodec,
)
from nyssa_ai_configuration.values.namespace_document_handler import (
    NamespaceDocumentHandler,
)
from nyssa_ai_configuration.values.types import ConfigurationRemoval, ConfigurationValue


class ConfigurationStore:
    """Resolves configuration entries across plaintext, aesLocal, oauth, and external cloud vaults."""

    def __init__(
        self,
        configuration_directory: str | Path,
        lock_timeout_ms: int = 10000,
        cipher: ConfigurationCipher | None = None,
        vaults: Mapping[str, CloudKeyVault] | None = None,
        credentials: Mapping[str, tuple[str, str]] | None = None,
        oauth_provider: OauthTokenProvider | None = None,
        recovery_protector: ConfigurationRecoveryTokenProtector | None = None,
        logger: ConfigurationLogger | None = None,
        clock: Callable[[], float] = time.monotonic,
    ) -> None:
        """Initialize store with directory, timeout, cipher, vaults, credentials, OAuth provider, recovery protector, and logger."""
        self._dir = Path(str(configuration_directory)).resolve()
        self._lock_timeout_ms = lock_timeout_ms
        self._cipher = cipher
        self._vaults: dict[str, CloudKeyVault] = dict(vaults) if vaults is not None else {}
        self._credentials: dict[str, tuple[str, str]] = dict(credentials) if credentials is not None else {}
        self._oauth = oauth_provider or OauthTokenProvider(cipher=cipher, logger=logger, clock=clock)
        self._logger = logger
        self._clock = clock
        self._write_interceptor: Callable[[str], Result[None]] | None = None
        self._resolving_credentials: set[str] = set()
        self._recovery = ConfigurationRecoveryCoordinator(
            document_factory=self._open_document,
            get_vault=self._get_vault,
            get_cipher=self._get_cipher,
            protector=recovery_protector,
            logger=logger,
        )
        self._mover = ConfigurationMover(
            document_factory=self._open_document,
            read_through=self._read_through,
            check_destination=self._check_destination,
            recovery_coordinator=self._recovery,
        )
        self._namespace_handler = NamespaceDocumentHandler(
            document_factory=self._open_document,
            read_through=self._read_through,
            check_destination=self._check_destination,
            recovery_coordinator=self._recovery,
        )

    def set_document_write_interceptor(self, interceptor: Callable[[str], Result[None]] | None = None) -> None:
        """Register an optional write interceptor used in recovery/OAuth tests to simulate disk write failures."""
        self._write_interceptor = interceptor

    def resolve_vault_credential(self, vault_name: str) -> Result[str]:
        """Resolve the bearer token configured for vault_name, blocking circular self-reference."""
        cred = self._credentials.get(vault_name)
        if cred is None:
            return fail(f"Provider '{vault_name}' declares no credential in the manifest.", "Validation")
        ns, key = cred
        if vault_name in self._resolving_credentials:
            return fail(f"Circular credential dependency detected for vault '{vault_name}' via '{ns}/{key}'.", "Conflict")
        self._resolving_credentials.add(vault_name)
        try:
            entry_res = self.get_entry(ns, key)
            if not entry_res.ok:
                return (
                    fail(f"Authorization required for provider '{vault_name}'; no credential found at '{ns}/{key}'.", "Forbidden")
                    if entry_res.category == "NotFound"
                    else entry_res
                )
            if entry_res.value.provider == vault_name:
                return fail(f"Credential '{ns}/{key}' for vault '{vault_name}' cannot be stored inside '{vault_name}' itself.", "Conflict")
            return self.get(ns, key)
        finally:
            self._resolving_credentials.discard(vault_name)

    def get_entry(self, namespace: str, key: str) -> Result[ConfigurationValue]:
        """Read the raw ConfigurationValue metadata and stored value for (namespace, key)."""
        valid = self._validate_address(namespace, key)
        if not valid.ok:
            return valid
        read_res = self._open_document(namespace).read()
        if not read_res.ok:
            return read_res
        entry = read_res.value.get(key)
        if entry is None:
            return fail(f"Configuration entry '{namespace}/{key}' was not found.", "NotFound")
        valid_entry = ConfigurationValueCodec.validate_entry(entry)
        return ok(entry) if valid_entry.ok else valid_entry

    def get(self, namespace: str, key: str) -> Result[str]:
        """Read and resolve the cleartext string value for (namespace, key) through its recorded provider."""
        started = self._clock()
        entry_res = self.get_entry(namespace, key)
        if not entry_res.ok:
            self._log_outcome("store.get", entry_res, started)
            return entry_res
        entry = entry_res.value
        res = ok(entry.value) if entry.is_literal else self._read_through(entry, namespace, key)
        self._log_outcome("store.get", res, started, dependency=entry.provider or "local")
        return res

    def set(self, namespace: str, key: str, value: str, provider: str | None = None, is_json: bool = False) -> Result[None]:
        """Write value to (namespace, key), preserving the existing provider when provider is None."""
        started = self._clock()
        valid = self._validate_address(namespace, key)
        if not valid.ok:
            self._log_outcome("store.set", valid, started)
            return valid
        if provider == "oauth":
            res: Result[None] = fail("The 'oauth' provider requires interactive authorization via authorize().", "Validation")
            self._log_outcome("store.set", res, started)
            return res

        def _mutator(entries: dict[str, ConfigurationValue]) -> Result[bool]:
            existing = entries.get(key)
            if existing is not None:
                valid_ex = ConfigurationValueCodec.validate_entry(existing)
                if not valid_ex.ok:
                    return valid_ex
                if existing.provider == "oauth":
                    return fail(f"Key '{namespace}/{key}' is managed by 'oauth'; use authorize() or reauthorize().", "Conflict")
                if provider is not None and provider != existing.provider:
                    return fail(
                        f"Key '{namespace}/{key}' is currently stored in provider '{existing.provider or 'local'}'; use move() to change providers.",
                        "Conflict",
                    )
                target_provider = existing.provider
            else:
                target_provider = provider or ""

            written = self._write_to_provider(target_provider, namespace, key, value, is_json)
            if not written.ok:
                return written
            entries[key] = written.value
            return ok(True)

        mod_res = self._open_document(namespace).modify(_mutator)
        out: Result[None] = ok(None) if mod_res.ok else mod_res
        self._log_outcome("store.set", out, started, dependency=provider or "local")
        return out

    def list_keys(self, namespace: str) -> Result[list[str]]:
        """Return all keys in namespace sorted in strict UTF-16 code-unit ordinal order."""
        valid = DirectorySafety.validate_segment(namespace, "namespace")
        if not valid.ok:
            return valid
        read_res = self._open_document(namespace).read()
        return ok(sorted(read_res.value.keys(), key=lambda s: s.encode("utf-16-be"))) if read_res.ok else read_res

    def remove(self, namespace: str, key: str) -> Result[ConfigurationRemoval]:
        """Remove (namespace, key) from its remote provider (if any) and from <namespace>.json."""
        started = self._clock()
        valid = self._validate_address(namespace, key)
        if not valid.ok:
            return valid
        removal_box: list[ConfigurationRemoval] = [ConfigurationRemoval(remote_value_remains=False)]

        def _mutator(entries: dict[str, ConfigurationValue]) -> Result[bool]:
            existing = entries.get(key)
            if existing is None:
                return ok(False)
            if existing.provider == "oauth":
                self._oauth.clear_held(namespace, key)
            elif existing.provider not in ("", "aesLocal"):
                vault_res = self._get_vault(existing.provider)
                if not vault_res.ok:
                    return vault_res
                rem_res = vault_res.value.remove(ConfigurationPointer(provider=existing.provider, value=existing.value))
                if not rem_res.ok:
                    return rem_res
                removal_box[0] = ConfigurationRemoval(remote_value_remains=rem_res.value)
            del entries[key]
            return ok(True)

        mod_res = self._open_document(namespace).modify(_mutator)
        res: Result[ConfigurationRemoval] = ok(removal_box[0]) if mod_res.ok else mod_res
        self._log_outcome("store.remove", res, started)
        return res

    def move(self, namespace: str, key: str, destination_provider: str) -> ConfigurationAttempt[ConfigurationMove]:
        """Migrate (namespace, key) to destination_provider using guarded two-phase recovery."""
        valid = self._validate_address(namespace, key)
        return self._mover.move(namespace, key, destination_provider) if valid.ok else ConfigurationAttempt(result=valid)

    def get_namespace(self, namespace: str) -> Result[str]:
        """Read and assemble all entries in <namespace>.json into a JSON object string."""
        valid = DirectorySafety.validate_segment(namespace, "namespace")
        return self._namespace_handler.get_namespace(namespace) if valid.ok else valid

    def set_namespace(self, namespace: str, serialized: str) -> ConfigurationAttempt[None]:
        """Replace <namespace>.json with top-level properties of serialized, preserving existing providers."""
        valid = DirectorySafety.validate_segment(namespace, "namespace")
        return self._namespace_handler.set_namespace(namespace, serialized, True) if valid.ok else ConfigurationAttempt(result=valid)

    def merge_settings(self, namespace: str, serialized: str) -> ConfigurationAttempt[None]:
        """Shallow-merge top-level properties of serialized into <namespace>.json."""
        valid = DirectorySafety.validate_segment(namespace, "namespace")
        return self._namespace_handler.set_namespace(namespace, serialized, False) if valid.ok else ConfigurationAttempt(result=valid)

    def authorize(self, namespace: str, key: str, options: OAuthOptions, browser_launcher: BrowserLauncher | None = None) -> Result[None]:
        """Run interactive PKCE OAuth authorization and store the sealed grant at (namespace, key)."""
        valid = self._validate_address(namespace, key)
        return self._oauth.authorize(namespace, key, options, self._open_document(namespace), browser_launcher) if valid.ok else valid

    def reauthorize(self, namespace: str, key: str, browser_launcher: BrowserLauncher | None = None) -> Result[None]:
        """Re-run interactive PKCE OAuth authorization using endpoint metadata stored at (namespace, key)."""
        valid = self._validate_address(namespace, key)
        return self._oauth.reauthorize(namespace, key, self._open_document(namespace), browser_launcher) if valid.ok else valid

    def recover(self, handle: ConfigurationRecoveryHandle) -> Result[ConfigurationRecoveryOutcome]:
        """Resume and reconcile an interrupted multi-step configuration operation."""
        return self._recovery.recover(handle)

    def _open_document(self, namespace: str) -> ConfigurationDocumentLocalFile:
        return ConfigurationDocumentLocalFile(
            self._dir, namespace, self._lock_timeout_ms, self._write_interceptor, self._logger, self._clock
        )

    def _read_through(self, entry: ConfigurationValue, namespace: str, key: str) -> Result[str]:
        if entry.provider == "aesLocal":
            c_res = self._get_cipher()
            return c_res.value.open(entry.value, namespace, key) if c_res.ok else c_res
        if entry.provider == "oauth":
            return self._oauth.read_access_token(namespace, key, entry, self._open_document(namespace))
        v_res = self._get_vault(entry.provider)
        return v_res.value.read(ConfigurationPointer(provider=entry.provider, value=entry.value)) if v_res.ok else v_res

    def _write_to_provider(self, provider_name: str, namespace: str, key: str, value: str, is_json: bool) -> Result[ConfigurationValue]:
        dest_ok = self._check_destination(provider_name, namespace, key)
        if not dest_ok.ok:
            return dest_ok
        if provider_name == "":
            return ok(ConfigurationValue(value=value, is_json_value=is_json))
        if provider_name == "aesLocal":
            c_res = self._get_cipher()
            if not c_res.ok:
                return c_res
            sealed = c_res.value.seal(value, namespace, key)
            return ok(ConfigurationValue(value=sealed.value, provider="aesLocal", encrypted=True, is_json_value=is_json)) if sealed.ok else sealed
        v_res = self._get_vault(provider_name)
        if not v_res.ok:
            return v_res
        ptr_res = v_res.value.write(namespace, key, value)
        return ok(ConfigurationValue(value=ptr_res.value.value, provider=provider_name, encrypted=False, is_json_value=is_json)) if ptr_res.ok else ptr_res

    def _check_destination(self, provider_name: str, namespace: str, key: str) -> Result[None]:
        if provider_name == "":
            return ok(None)
        if provider_name == "aesLocal":
            c_res = self._get_cipher()
            return ok(None) if c_res.ok else c_res
        v_res = self._get_vault(provider_name)
        if not v_res.ok:
            return v_res
        if self._credentials.get(provider_name) == (namespace, key):
            return fail(
                f"Circular credential rejected: '{namespace}/{key}' is the authentication credential for vault '{provider_name}' and cannot be stored inside '{provider_name}'.",
                "Conflict",
            )
        return ok(None)

    def _get_vault(self, provider_name: str) -> Result[CloudKeyVault]:
        vault = self._vaults.get(provider_name)
        return ok(vault) if vault is not None else fail(f"Configuration provider '{provider_name}' is not registered.", "Validation")

    def _get_cipher(self) -> Result[ConfigurationCipher]:
        return ok(self._cipher) if self._cipher is not None else fail("Local encryption cipher is not configured.", "Unavailable")

    @staticmethod
    def _validate_address(namespace: str, key: str) -> Result[None]:
        ns_ok = DirectorySafety.validate_segment(namespace, "namespace")
        if not ns_ok.ok:
            return ns_ok
        return ok(None) if key and key.strip() else fail("Configuration key cannot be blank.", "Validation")

    def _log_outcome(self, operation: str, res: Result[object], started: float, dependency: str = "local") -> None:
        if self._logger is None:
            return
        elapsed_ms = (self._clock() - started) * 1000.0
        if res.ok:
            self._logger.completed(operation, "success", elapsed_ms, "none", dependency, 1)
        elif res.category in ("Validation", "Conflict", "NotFound", "Forbidden"):
            self._logger.completed(operation, "rejected", elapsed_ms, res.category.lower(), dependency, 1)
        else:
            self._logger.completed(operation, "failure", elapsed_ms, res.category.lower(), dependency, 1)
