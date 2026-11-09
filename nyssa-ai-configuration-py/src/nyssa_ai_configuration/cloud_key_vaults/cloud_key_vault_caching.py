# Responsibility: Decorator caching CloudKeyVault secret reads with a configurable TTL (cacheTTLSeconds) and pluggable SecretCache.
# Public interface:
# `-- CloudKeyVaultCaching
#     +-- DEFAULT_CACHE_TTL_SECONDS
#     +-- cache_scope
#     +-- cache_ttl_seconds
#     +-- clear_cache()
#     +-- execute(operation, preparation)
#     +-- name
#     +-- new(inner, cache_ttl_seconds, cache, cache_scope, clock)
#     +-- prepare(operation)
#     +-- read(pointer)
#     +-- reconcile(operation, preparation)
#     +-- recovery
#     +-- remove(pointer)
#     +-- resolve_ttl_seconds(options)
#     +-- store_identity
#     +-- wrap(inner, options, cache, clock)
#     `-- write(namespace, key, value)
"""Configurable TTL caching decorator for CloudKeyVault implementations."""

from __future__ import annotations

from collections.abc import Callable
import time
from typing import final

from nyssa_ai_configuration.cloud_key_vaults.bitwarden.cloud_key_vault_bitwarden import (
    CloudKeyVaultBitwarden,
)
from nyssa_ai_configuration.cloud_key_vaults.cloud_key_vault import CloudKeyVault
from nyssa_ai_configuration.cloud_key_vaults.cloud_key_vault_recovery import (
    CloudKeyVaultRecovery,
)
from nyssa_ai_configuration.cloud_key_vaults.secret_cache import SecretCache
from nyssa_ai_configuration.cloud_key_vaults.secret_cache_in_memory import (
    SecretCacheInMemory,
)
from nyssa_ai_configuration.cloud_key_vaults.types import (
    CloudKeyVaultOperation,
    CloudKeyVaultPreparation,
    CloudKeyVaultStepEffect,
    ConfigurationPointer,
)
from nyssa_ai_configuration.manifest.types import VaultOptions
from nyssa_ai_result import Result, fail, ok


@final
class CloudKeyVaultCaching:
    """Decorates a CloudKeyVault with a TTL secret cache and automatic write/remove/recovery invalidation."""

    DEFAULT_CACHE_TTL_SECONDS: int = 60

    def __init__(
        self,
        inner: CloudKeyVault,
        cache_ttl_seconds: int = DEFAULT_CACHE_TTL_SECONDS,
        cache: SecretCache | None = None,
        cache_scope: str | None = None,
        clock: Callable[[], float] = time.monotonic,
    ) -> None:
        """Initialize decorator wrapping inner vault with cache_ttl_seconds and optional SecretCache."""
        self._inner = inner
        self._cache_ttl_seconds = max(0, int(cache_ttl_seconds))
        self._cache: SecretCache = cache if cache is not None else SecretCacheInMemory(clock=clock)
        scope = (cache_scope or "").strip()
        self._cache_scope = scope if scope else inner.name
        if self._cache_ttl_seconds > 0 and isinstance(inner, CloudKeyVaultBitwarden):
            inner.set_secret_listed_callback(self._on_secret_listed)

    @property
    def name(self) -> str:
        """Return the configured registration name of the wrapped vault instance."""
        return self._inner.name

    @property
    def cache_ttl_seconds(self) -> int:
        """Return the configured cache TTL in seconds (0 when caching is disabled)."""
        return self._cache_ttl_seconds

    @property
    def cache_scope(self) -> str:
        """Return the scope prefix isolating cache entries across vault targets."""
        return self._cache_scope

    @property
    def recovery(self) -> CloudKeyVaultRecovery | None:
        """Return this decorator as CloudKeyVaultRecovery when inner vault supports two-phase recovery."""
        return self if self._inner.recovery is not None else None

    @property
    def store_identity(self) -> str:
        """Return the stable backend store identity from the inner vault's recovery contract."""
        inner_rec = self._inner.recovery
        return inner_rec.store_identity if inner_rec is not None else ""

    @classmethod
    def resolve_ttl_seconds(cls, options: VaultOptions) -> Result[int]:
        """Resolve and validate cacheTTLSeconds from VaultOptions or settings['cacheTTLSeconds']."""
        if options.cache_ttl_seconds is not None:
            if isinstance(options.cache_ttl_seconds, bool) or options.cache_ttl_seconds < 0:
                return fail(
                    f"Vault '{options.name}' cacheTTLSeconds must be a non-negative integer.",
                    "Validation",
                )
            return ok(options.cache_ttl_seconds)

        raw_setting = options.settings.get("cacheTTLSeconds")
        if raw_setting is not None:
            trimmed = raw_setting.strip()
            if not trimmed or not (trimmed.isdigit() or (trimmed.startswith("-") and trimmed[1:].isdigit())):
                return fail(
                    f"Vault '{options.name}' settings['cacheTTLSeconds'] must be a non-negative integer.",
                    "Validation",
                )
            parsed = int(trimmed)
            if parsed < 0:
                return fail(
                    f"Vault '{options.name}' settings['cacheTTLSeconds'] must be a non-negative integer.",
                    "Validation",
                )
            return ok(parsed)

        return ok(cls.DEFAULT_CACHE_TTL_SECONDS)

    @classmethod
    def wrap(
        cls,
        inner: CloudKeyVault,
        options: VaultOptions,
        cache: SecretCache | None = None,
        clock: Callable[[], float] = time.monotonic,
    ) -> Result[CloudKeyVault]:
        """Validate cacheTTLSeconds on options and wrap inner in CloudKeyVaultCaching when TTL > 0."""
        ttl_res = cls.resolve_ttl_seconds(options)
        if not ttl_res.ok:
            return ttl_res
        if ttl_res.value == 0:
            return ok(inner)
        target_id = (
            (options.settings.get("projectId") or "").strip()
            or (options.endpoint or "").strip()
            or (options.settings.get("serverUrl") or "").strip()
            or (options.settings.get("vaultUri") or "").strip()
        )
        scope = f"{options.name}:{options.kind}:{target_id}" if target_id else f"{options.name}:{options.kind}"
        return ok(
            cls(
                inner=inner,
                cache_ttl_seconds=ttl_res.value,
                cache=cache,
                cache_scope=scope,
                clock=clock,
            )
        )

    def read(self, pointer: ConfigurationPointer) -> Result[str]:
        """Read secret for pointer from cache when unexpired, otherwise read from inner vault and populate cache."""
        if self._cache_ttl_seconds <= 0 or not pointer.value or not pointer.value.strip():
            return self._inner.read(pointer)

        provider = pointer.provider.strip() if pointer.provider and pointer.provider.strip() else self._inner.name
        cache_key = self._format_key(provider, pointer.value.strip())
        cached = self._safe_get(cache_key)
        if cached is not None:
            return ok(cached)

        res = self._inner.read(pointer)
        if res.ok and isinstance(res.value, str):
            self._safe_set(cache_key, res.value)
        return res

    def write(self, namespace: str, key: str, value: str) -> Result[ConfigurationPointer]:
        """Write secret to inner vault and evict any cached entry for (namespace, key) and returned pointer."""
        self._evict_pointer(f"{namespace.strip()}/{key.strip()}")
        res = self._inner.write(namespace, key, value)
        if res.ok:
            self._evict_pointer(res.value.value)
        return res

    def remove(self, pointer: ConfigurationPointer) -> Result[bool]:
        """Remove pointer from inner vault and evict its cached value."""
        if pointer.value:
            self._evict_pointer(pointer.value)
        return self._inner.remove(pointer)

    def clear_cache(self) -> None:
        """Evict all entries from the underlying SecretCache (fail-open)."""
        try:
            self._cache.clear()
        except Exception:
            return

    def prepare(self, operation: CloudKeyVaultOperation) -> Result[CloudKeyVaultPreparation]:
        """Delegate two-phase recovery preparation to the inner vault."""
        inner_rec = self._inner.recovery
        if inner_rec is None:
            return fail(f"Vault '{self._inner.name}' does not support two-phase recovery.", "Validation")
        return inner_rec.prepare(operation)

    def execute(
        self,
        operation: CloudKeyVaultOperation,
        preparation: CloudKeyVaultPreparation,
    ) -> Result[CloudKeyVaultStepEffect]:
        """Execute two-phase vault mutation on inner vault and evict affected cache keys."""
        inner_rec = self._inner.recovery
        if inner_rec is None:
            return fail(f"Vault '{self._inner.name}' does not support two-phase recovery.", "Validation")
        self._evict_recovery_keys(operation, preparation)
        res = inner_rec.execute(operation, preparation)
        if res.ok and res.value.pointer is not None:
            self._evict_pointer(res.value.pointer.value)
        return res

    def reconcile(
        self,
        operation: CloudKeyVaultOperation,
        preparation: CloudKeyVaultPreparation,
    ) -> Result[CloudKeyVaultStepEffect]:
        """Reconcile interrupted two-phase vault mutation on inner vault and evict affected cache keys."""
        inner_rec = self._inner.recovery
        if inner_rec is None:
            return fail(f"Vault '{self._inner.name}' does not support two-phase recovery.", "Validation")
        self._evict_recovery_keys(operation, preparation)
        res = inner_rec.reconcile(operation, preparation)
        if res.ok and res.value.pointer is not None:
            self._evict_pointer(res.value.pointer.value)
        return res

    def _on_secret_listed(self, pointer_value: str, secret_value: str) -> None:
        if self._cache_ttl_seconds <= 0 or not pointer_value.strip():
            return
        self._safe_set(self._format_key(self._inner.name, pointer_value.strip()), secret_value)

    def _evict_recovery_keys(
        self,
        operation: CloudKeyVaultOperation,
        preparation: CloudKeyVaultPreparation,
    ) -> None:
        self._evict_pointer(f"{operation.namespace.strip()}/{operation.key.strip()}")
        if operation.pointer is not None and operation.pointer.value:
            self._evict_pointer(operation.pointer.value)
        if preparation.target_reference:
            self._evict_pointer(preparation.target_reference)

    def _evict_pointer(self, pointer_value: str) -> None:
        trimmed = pointer_value.strip()
        if not trimmed:
            return
        self._safe_remove(self._format_key(self._inner.name, trimmed))

    def _format_key(self, provider: str, pointer_value: str) -> str:
        return f"{self._cache_scope}:{provider}:{pointer_value}"

    def _safe_get(self, key: str) -> str | None:
        try:
            return self._cache.get(key)
        except Exception:
            return None

    def _safe_set(self, key: str, value: str) -> None:
        try:
            self._cache.set(key, value, self._cache_ttl_seconds)
        except Exception:
            return

    def _safe_remove(self, key: str) -> None:
        try:
            self._cache.remove(key)
        except Exception:
            return
