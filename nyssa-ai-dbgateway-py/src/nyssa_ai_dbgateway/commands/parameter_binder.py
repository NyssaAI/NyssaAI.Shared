# Responsibility: Provide parameter binding contract for typed commands and queries.
# Public interface:
# `-- ParameterBinder
#     +-- add(name, value)
#     `-- add_many(parameters)
"""Parameter binder protocol."""

from typing import Mapping, Protocol


class ParameterBinder(Protocol):
    """Binds named parameters for execution."""

    def add(self, name: str, value: object) -> None:
        """Add a named parameter with automatic coercion."""
        ...

    def add_many(self, parameters: Mapping[str, object]) -> None:
        """Add multiple named parameters."""
        ...
