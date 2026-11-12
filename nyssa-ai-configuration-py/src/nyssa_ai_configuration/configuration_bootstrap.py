# Responsibility: Resolve manifest, unseal root keyVaults secrets, layer vaults, and wire ConfigurationStore for bootstrap.
# Public interface:
# `-- ConfigurationBootstrap
#     `-- build_store(options, keyring, vault_factories, oauth_flow_client, browser_launcher, logger, clock, secret_cache, manifest_keyring)
"""Bootstrap pipeline resolving manifest, keyring custody, encrypted vault envelopes, and multi-vault wiring."""

from __future__ import annotations

from collections.abc import Callable, Mapping, Sequence
import dataclasses
from pathlib import Path
import time

from nyssa_ai_configuration.cloud_key_vaults.bitwarden.cloud_key_vault_factory_bitwarden import (
    CloudKeyVaultFactoryBitwarden,
)
from nyssa_ai_configuration.cloud_key_vaults.cloud_key_vault import CloudKeyVault
from nyssa_ai_configuration.cloud_key_vaults.cloud_key_vault_caching import CloudKeyVaultCaching
from nyssa_ai_configuration.cloud_key_vaults.cloud_key_vault_factory import CloudKeyVaultFactory
from nyssa_ai_configuration.cloud_key_vaults.secret_cache import SecretCache
from nyssa_ai_configuration.encryption.configuration_cipher_aes_gcm import ConfigurationCipherAesGcm
from nyssa_ai_configuration.file_security.directory_safety import DirectorySafety
from nyssa_ai_configuration.file_security.protected_file_system import ProtectedFileSystem
from nyssa_ai_configuration.keyring.keyring import (
    MANIFEST_KEY_ACCOUNT,
    Keyring,
    get_existing_key,
    get_or_create_key,
    resolve_keyring,
    resolve_manifest_keyring,
)
from nyssa_ai_configuration.logging.configuration_logger import ConfigurationLogger
from nyssa_ai_configuration.manifest.configuration_manifest_reader import ConfigurationManifestReader
from nyssa_ai_configuration.manifest.configuration_manifest_writer import ConfigurationManifestWriter
from nyssa_ai_configuration.manifest.types import BootstrapOptions, VaultManifestEntry, VaultOptions
from nyssa_ai_configuration.oauth.browser_launcher import BrowserLauncher
from nyssa_ai_configuration.oauth.oauth_flow_client import OauthFlowClient
from nyssa_ai_configuration.oauth.oauth_token_provider import OauthTokenProvider
from nyssa_ai_configuration.oauth.types import OAuthOptions
from nyssa_ai_configuration.recovery.configuration_recovery_token_protector import (
    ConfigurationRecoveryTokenProtector,
)
from nyssa_ai_configuration.values.configuration_store import ConfigurationStore
from nyssa_ai_result import Result, fail, ok


class ConfigurationBootstrap:
    """Executes the ordered startup sequence for ConfigurationClient.create."""

    @classmethod
    def build_store(
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
    ) -> Result[tuple[str, str, int, ConfigurationStore, dict[str, tuple[str, str, OAuthOptions | None]]]]:
        """Resolve manifest, unlock keys, unseal root keyVaults, wire multi-vault store, and return bootstrap tuple."""
        app_name_res = DirectorySafety.validate_segment(options.application_name, "application_name")
        if not app_name_res.ok:
            return app_name_res
        app_name = app_name_res.value

        prog_res = cls._collect_programmatic_vaults(options)
        if not prog_res.ok:
            return prog_res
        effective_vaults = prog_res.value

        manifest_path = (
            Path(options.manifest_path).resolve()
            if options.manifest_path and options.manifest_path.strip()
            else ConfigurationManifestReader.default_manifest_path(options.home_directory)
        )
        doc_res = ConfigurationManifestReader.read_manifest(manifest_path)
        if not doc_res.ok and doc_res.category != "NotFound":
            return doc_res

        manifest_vaults = doc_res.value.key_vaults if doc_res.ok else {}
        existing = doc_res.value.applications.get(app_name) if doc_res.ok else None
        if existing is not None:
            config_dir = existing.configuration_directory
            timeout_ms = options.lock_timeout_ms if options.lock_timeout_ms is not None else existing.lock_timeout_ms
            if timeout_ms <= 0:
                return fail("lockTimeoutMs must be a positive integer.", "Validation")
        else:
            config_dir = (
                str(Path(options.configuration_directory).resolve())
                if options.configuration_directory and options.configuration_directory.strip()
                else str((manifest_path.parent / app_name).resolve())
            )
            timeout_ms = options.lock_timeout_ms if options.lock_timeout_ms is not None else 10000
            if timeout_ms <= 0:
                return fail("lockTimeoutMs must be a positive integer.", "Validation")
            prov_res = ConfigurationManifestWriter(logger=logger, clock=clock).provision_application(
                manifest_path, app_name, config_dir, timeout_ms
            )
            if not prov_res.ok:
                return prov_res

        unsealed_res = cls._unseal_manifest_vaults(manifest_path, manifest_vaults, keyring, manifest_keyring)
        if not unsealed_res.ok:
            return unsealed_res
        for v_name, m_vault in unsealed_res.value.items():
            code_vault = effective_vaults.get(v_name)
            if code_vault is not None and m_vault.cache_ttl_seconds is None and code_vault.cache_ttl_seconds is not None:
                m_vault = dataclasses.replace(m_vault, cache_ttl_seconds=code_vault.cache_ttl_seconds)
            effective_vaults[v_name] = m_vault

        dir_safe = DirectorySafety.verify(config_dir)
        if not dir_safe.ok:
            return dir_safe
        dir_created = ProtectedFileSystem.create_directory(config_dir)
        if not dir_created.ok:
            return dir_created

        active_keyring = resolve_keyring(config_dir, options, keyring, manifest_directory=manifest_path.parent)
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
        wired_res = cls._wire_vaults(effective_vaults, vault_factories, store_box, secret_cache, clock)
        if not wired_res.ok:
            return wired_res
        vaults, credentials, vault_auth = wired_res.value

        store = ConfigurationStore(
            config_dir, timeout_ms, cipher, vaults, credentials, oauth_provider, protector, logger, clock
        )
        store_box.append(store)
        return ok((app_name, config_dir, timeout_ms, store, vault_auth))

    @staticmethod
    def _collect_programmatic_vaults(options: BootstrapOptions) -> Result[dict[str, VaultOptions]]:
        out: dict[str, VaultOptions] = {}
        raw_list: list[VaultOptions] = (
            ([options.key_vault] if options.key_vault is not None else []) + list(options.key_vaults)
        )
        for item in raw_list:
            valid_res = ConfigurationManifestReader.validate_vault_options(item)
            if not valid_res.ok:
                return valid_res
            norm = valid_res.value
            if norm.name in out:
                return fail(f"Duplicate programmatic key vault name '{norm.name}'.", "Validation")
            out[norm.name] = norm
        return ok(out)

    @staticmethod
    def _unseal_manifest_vaults(
        manifest_path: Path,
        manifest_vaults: Mapping[str, VaultManifestEntry],
        keyring: Keyring | None,
        manifest_keyring: Keyring | None,
    ) -> Result[dict[str, VaultOptions]]:
        if not any(len(v.secrets) > 0 for v in manifest_vaults.values()):
            return ok(
                {
                    name: VaultOptions(v.name, v.kind, v.endpoint, v.credential, dict(v.settings), {}, v.cache_ttl_seconds)
                    for name, v in manifest_vaults.items()
                }
            )

        active_kr = (
            manifest_keyring
            if manifest_keyring is not None
            else (keyring if keyring is not None else resolve_manifest_keyring(manifest_path))
        )
        mkey_res = get_existing_key(MANIFEST_KEY_ACCOUNT, active_kr)
        if not mkey_res.ok:
            if mkey_res.category == "NotFound":
                return fail(
                    "configuration: manifest encryption key 'nyssa-ai-configuration/_manifest' was not found in the keyring.",
                    "Validation",
                )
            return mkey_res
        if mkey_res.value is None:
            return fail(
                "configuration: manifest encryption key 'nyssa-ai-configuration/_manifest' was not found in the keyring.",
                "Validation",
            )

        key_buf = bytearray(mkey_res.value)
        try:
            mcipher_res = ConfigurationCipherAesGcm.create(bytes(key_buf))
            if not mcipher_res.ok:
                return mcipher_res
            mcipher = mcipher_res.value
            unsealed: dict[str, VaultOptions] = {}
            for v_name, v_entry in manifest_vaults.items():
                dec_secrets: dict[str, str] = {}
                aad_ns = f"keyVaults/{v_name}"
                for sk, env in v_entry.secrets.items():
                    opened = mcipher.open(env.value, aad_ns, sk)
                    if not opened.ok:
                        return fail(
                            f"manifest: failed to decrypt keyVaults.{v_name}.secrets.{sk}: {opened.message}",
                            "Validation",
                        )
                    dec_secrets[sk] = opened.value
                unsealed[v_name] = VaultOptions(
                    v_entry.name,
                    v_entry.kind,
                    v_entry.endpoint,
                    v_entry.credential,
                    dict(v_entry.settings),
                    dec_secrets,
                    v_entry.cache_ttl_seconds,
                )
            return ok(unsealed)
        finally:
            for idx in range(len(key_buf)):
                key_buf[idx] = 0

    @staticmethod
    def _wire_vaults(
        effective_vaults: Mapping[str, VaultOptions],
        vault_factories: Sequence[CloudKeyVaultFactory],
        store_box: list[ConfigurationStore],
        secret_cache: SecretCache | None,
        clock: Callable[[], float],
    ) -> Result[
        tuple[
            dict[str, CloudKeyVault],
            dict[str, tuple[str, str]],
            dict[str, tuple[str, str, OAuthOptions | None]],
        ]
    ]:
        vaults: dict[str, CloudKeyVault] = {}
        credentials: dict[str, tuple[str, str]] = {}
        vault_auth: dict[str, tuple[str, str, OAuthOptions | None]] = {}

        class _StoreTokenSource:
            def __init__(self, vault_name: str) -> None:
                self._vault_name = vault_name

            def get_access_token(self) -> Result[str]:
                return store_box[0].resolve_vault_credential(self._vault_name)

        for vault_opt in effective_vaults.values():
            factory: CloudKeyVaultFactory | None = next((f for f in vault_factories if f.kind == vault_opt.kind), None)
            if factory is None and vault_opt.kind in ("bitwarden-secrets", "bws", "bitwarden"):
                factory = CloudKeyVaultFactoryBitwarden(vault_opt.kind)
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
            wrapped_vault = CloudKeyVaultCaching.wrap(created_vault.value, vault_opt, cache=secret_cache, clock=clock)
            if not wrapped_vault.ok:
                return wrapped_vault
            vaults[vault_opt.name] = wrapped_vault.value
            auth_opts_res = factory.authorization(vault_opt)
            if not auth_opts_res.ok:
                return auth_opts_res
            if cred_ns and cred_key:
                vault_auth[vault_opt.name] = (cred_ns, cred_key, auth_opts_res.value)
        return ok((vaults, credentials, vault_auth))
