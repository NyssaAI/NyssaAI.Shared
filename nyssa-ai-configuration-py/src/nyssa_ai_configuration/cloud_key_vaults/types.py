# Responsibility: Define cloud key vault pointer, operation, preparation, and step effect data structures.
# Public interface:
# +-- CloudKeyVaultAction
# +-- CloudKeyVaultOperation
# |   +-- action
# |   +-- key
# |   +-- namespace
# |   +-- operation_id
# |   +-- pointer
# |   `-- value
# +-- CloudKeyVaultPreparation
# |   +-- precondition
# |   +-- store_identity
# |   `-- target_reference
# +-- CloudKeyVaultStepEffect
# |   +-- pointer
# |   `-- state
# +-- ConfigurationPointer
# |   +-- provider
# |   `-- value
# `-- ConfigurationStepState
"""Cloud key vault data contracts and step states."""

from dataclasses import dataclass, field
from typing import Literal

type CloudKeyVaultAction = Literal["Write", "Remove"]
type ConfigurationStepState = Literal[
    "Pending",
    "Applied",
    "Failed",
    "Unknown",
    "Conflict",
    "Retained",
    "Manual",
]


@dataclass(frozen=True, slots=True)
class ConfigurationPointer:
    """Local reference stored in <namespace>.json pointing to a secret in an external cloud vault."""

    provider: str
    value: str


@dataclass(frozen=True, slots=True)
class CloudKeyVaultOperation:
    """Describes a single remote vault write or removal operation."""

    operation_id: str
    action: CloudKeyVaultAction
    namespace: str
    key: str
    value: str | None = field(default=None, repr=False)
    pointer: ConfigurationPointer | None = None


@dataclass(frozen=True, slots=True)
class CloudKeyVaultPreparation:
    """Read-only preparation snapshot captured before a guarded cloud vault mutation."""

    store_identity: str
    target_reference: str | None = None
    precondition: str | None = None


@dataclass(frozen=True, slots=True)
class CloudKeyVaultStepEffect:
    """Outcome of executing or reconciling a guarded cloud vault step."""

    state: ConfigurationStepState
    pointer: ConfigurationPointer | None = None
