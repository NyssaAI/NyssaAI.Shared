# Responsibility: Define immutable configuration value entry and removal outcome records.
# Public interface:
# +-- ConfigurationRemoval
# |   `-- remote_value_remains
# `-- ConfigurationValue
#     +-- encrypted
#     +-- is_json_value
#     +-- is_literal
#     +-- provider
#     +-- reauth_required
#     `-- value
"""Value and removal records for ConfigurationStore."""

from dataclasses import dataclass


@dataclass(frozen=True, slots=True)
class ConfigurationValue:
    """Represents a stored configuration entry inside <namespace>.json."""

    value: str
    provider: str = ""
    encrypted: bool = False
    reauth_required: bool = False
    is_json_value: bool = False

    @property
    def is_literal(self) -> bool:
        """Return True when the entry is stored directly in the local document without a provider."""
        return len(self.provider) == 0


@dataclass(frozen=True, slots=True)
class ConfigurationRemoval:
    """Outcome of removing a configuration entry."""

    remote_value_remains: bool = False
