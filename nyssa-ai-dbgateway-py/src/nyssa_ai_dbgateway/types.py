# Responsibility: Group pure DTOs, type aliases, and literal types without behavior.
# Public interface:
# +-- DbDialect
# `-- DbGatewayConfiguration
#     +-- command_timeout_seconds
#     +-- connection_string
#     `-- database_provider
"""Pure data types and literals for dbgateway."""

from dataclasses import dataclass
from typing import Literal

DbDialect = Literal["sqlite", "postgres"]


@dataclass(frozen=True, slots=True)
class DbGatewayConfiguration:
    """Carries database gateway configuration settings with no external I/O."""

    database_provider: str
    connection_string: str
    command_timeout_seconds: int = 30
