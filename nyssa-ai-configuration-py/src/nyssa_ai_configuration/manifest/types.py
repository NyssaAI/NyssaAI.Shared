# Responsibility: Define immutable manifest, vault option, and bootstrap option data structures.
# Public interface:
# +-- ApplicationManifestEntry
# |   +-- configuration_directory
# |   `-- lock_timeout_ms
# +-- BootstrapOptions
# |   +-- application_name
# |   +-- configuration_directory
# |   +-- home_directory
# |   +-- key_vault
# |   +-- key_vaults
# |   +-- lock_timeout_ms
# |   +-- manifest_path
# |   `-- systemd_credential_name
# +-- ConfigurationManifestDocument
# |   +-- applications
# |   +-- key_vaults
# |   `-- version
# +-- ManifestSecretEnvelope
# |   +-- encrypted
# |   +-- provider
# |   `-- value
# +-- VaultManifestEntry
# |   +-- cache_ttl_seconds
# |   +-- credential
# |   +-- endpoint
# |   +-- kind
# |   +-- name
# |   +-- secrets
# |   `-- settings
# `-- VaultOptions
#     +-- cache_ttl_seconds
#     +-- credential
#     +-- endpoint
#     +-- kind
#     +-- name
#     +-- secrets
#     `-- settings
"""Manifest and bootstrap option data structures."""

from collections.abc import Mapping, Sequence
from dataclasses import dataclass, field


@dataclass(frozen=True, slots=True)
class ManifestSecretEnvelope:
    """Encrypted FormatVersion 2 secret envelope stored under keyVaults.<name>.secrets in config-manifest.json."""

    value: str = field(repr=False)
    encrypted: bool = True
    provider: str = "aesLocal"


@dataclass(frozen=True, slots=True)
class VaultManifestEntry:
    """Parsed root keyVaults.<name> entry in config-manifest.json with encrypted secret envelopes."""

    name: str
    kind: str
    endpoint: str | None = None
    credential: str | None = None
    settings: Mapping[str, str] = field(default_factory=dict)
    secrets: Mapping[str, ManifestSecretEnvelope] = field(default_factory=dict, repr=False)
    cache_ttl_seconds: int | None = None


@dataclass(frozen=True, slots=True)
class VaultOptions:
    """Configuration options for an external cloud key vault provider (secrets omitted from repr)."""

    name: str
    kind: str
    endpoint: str | None = None
    credential: str | None = None
    settings: Mapping[str, str] = field(default_factory=dict)
    secrets: Mapping[str, str] = field(default_factory=dict, repr=False)
    cache_ttl_seconds: int | None = None


@dataclass(frozen=True, slots=True)
class ApplicationManifestEntry:
    """Resolved bootstrap entry for a single application inside config-manifest.json."""

    configuration_directory: str
    lock_timeout_ms: int = 10000


@dataclass(frozen=True, slots=True)
class ConfigurationManifestDocument:
    """Parsed version 1 configuration manifest document."""

    version: int = 1
    applications: Mapping[str, ApplicationManifestEntry] = field(default_factory=dict)
    key_vaults: Mapping[str, VaultManifestEntry] = field(default_factory=dict)


@dataclass(frozen=True, slots=True)
class BootstrapOptions:
    """Caller-supplied bootstrap options resolved alongside config-manifest.json."""

    application_name: str
    manifest_path: str | None = None
    home_directory: str | None = None
    configuration_directory: str | None = None
    lock_timeout_ms: int | None = None
    key_vault: VaultOptions | None = None
    key_vaults: Sequence[VaultOptions] = ()
    systemd_credential_name: str | None = None
