# Responsibility: Define payload-free operation progress receipts, recovery outcomes, move results, and encrypted replay state records.
# Public interface:
# +-- ConfigurationAttempt
# |   +-- progress
# |   +-- recovery_handle
# |   `-- result
# +-- ConfigurationMove
# |   +-- progress
# |   +-- recovery_handle
# |   +-- source_delete_error
# |   `-- source_value_remains
# +-- ConfigurationOperationKind
# +-- ConfigurationOperationProgress
# |   +-- kind
# |   +-- namespace
# |   +-- operation_id
# |   +-- publication
# |   +-- recovery
# |   `-- steps
# +-- ConfigurationPublicationState
# +-- ConfigurationRecoveryOutcome
# |   +-- progress
# |   +-- recovery_handle
# |   `-- status
# +-- ConfigurationRecoveryState
# |   +-- conflicted
# |   +-- kind
# |   +-- namespace
# |   +-- operation_id
# |   +-- original
# |   +-- proposed
# |   +-- publication
# |   +-- steps
# |   `-- version
# +-- ConfigurationRecoveryStatus
# +-- ConfigurationRecoveryStep
# |   +-- is_json_value
# |   +-- local
# |   +-- operation
# |   +-- output
# |   +-- preparation
# |   +-- provider
# |   +-- source
# |   +-- state
# |   `-- target_reference
# `-- ConfigurationStepProgress
#     +-- action
#     +-- key
#     +-- operation_id
#     +-- provider
#     +-- state
#     `-- target_reference
"""Recovery progress receipts, outcomes, and state data structures."""

from collections.abc import Mapping
from dataclasses import dataclass, field
from typing import Literal

from nyssa_ai_configuration.cloud_key_vaults.types import (
    CloudKeyVaultAction,
    CloudKeyVaultOperation,
    CloudKeyVaultPreparation,
    ConfigurationStepState,
)
from nyssa_ai_configuration.recovery.configuration_recovery_handle import (
    ConfigurationRecoveryHandle,
)
from nyssa_ai_configuration.results.result import Result
from nyssa_ai_configuration.values.types import ConfigurationValue

type ConfigurationOperationKind = Literal["Move", "Replace", "Merge"]
type ConfigurationPublicationState = Literal["NotAttempted", "Published", "Failed", "Unknown"]
type ConfigurationRecoveryStatus = Literal["Completed", "Unfinished", "Conflict", "ManualReconciliation"]


@dataclass(frozen=True, slots=True)
class ConfigurationStepProgress:
    """Payload-free diagnostic receipt for a single key step in a multi-step operation."""

    operation_id: str
    key: str
    provider: str
    action: CloudKeyVaultAction
    state: ConfigurationStepState
    target_reference: str | None = None


@dataclass(frozen=True, slots=True)
class ConfigurationOperationProgress:
    """Payload-free progress report for a Move, Replace, or Merge operation."""

    operation_id: str
    namespace: str
    kind: ConfigurationOperationKind
    publication: ConfigurationPublicationState
    recovery: ConfigurationRecoveryStatus
    steps: tuple[ConfigurationStepProgress, ...] = field(default_factory=tuple)


@dataclass(frozen=True, slots=True)
class ConfigurationRecoveryOutcome:
    """Result returned by recover(handle) describing final or remaining work."""

    status: ConfigurationRecoveryStatus
    progress: ConfigurationOperationProgress
    recovery_handle: ConfigurationRecoveryHandle | None = None


@dataclass(frozen=True, slots=True)
class ConfigurationMove:
    """Outcome of moving a configuration entry between providers."""

    source_value_remains: bool = False
    source_delete_error: str | None = None
    progress: ConfigurationOperationProgress | None = None
    recovery_handle: ConfigurationRecoveryHandle | None = None


@dataclass(frozen=True, slots=True)
class ConfigurationAttempt[T]:
    """Wraps a Result alongside optional operation progress and a recovery handle when interrupted."""

    result: Result[T]
    progress: ConfigurationOperationProgress | None = None
    recovery_handle: ConfigurationRecoveryHandle | None = None


@dataclass(frozen=True, slots=True)
class ConfigurationRecoveryStep:
    """Internal replay step stored inside an encrypted ConfigurationRecoveryState."""

    provider: str
    operation: CloudKeyVaultOperation
    local: bool = False
    is_json_value: bool = False
    preparation: CloudKeyVaultPreparation | None = None
    target_reference: str | None = None
    output: ConfigurationValue | None = field(default=None, repr=False)
    source: ConfigurationValue | None = field(default=None, repr=False)
    state: ConfigurationStepState = "Pending"


@dataclass(frozen=True, slots=True)
class ConfigurationRecoveryState:
    """Encrypted replay state sealed inside a ConfigurationRecoveryHandle."""

    operation_id: str
    namespace: str
    kind: ConfigurationOperationKind
    original: Mapping[str, ConfigurationValue] = field(repr=False)
    proposed: Mapping[str, ConfigurationValue] = field(repr=False)
    steps: tuple[ConfigurationRecoveryStep, ...] = field(repr=False)
    publication: ConfigurationPublicationState = "NotAttempted"
    conflicted: bool = False
    version: int = 1
