# Responsibility: Carries provider registration mapping database provider names to factory functions.
# Public interface:
# `-- DbProviderRegistration
#     +-- database_provider
#     `-- prepare
"""Database provider registration DTO."""

from dataclasses import dataclass
from typing import Callable
from nyssa_ai_dbgateway.types import DbGatewayConfiguration
from nyssa_ai_dbgateway.providers.db_provider import DbProvider


@dataclass(frozen=True, slots=True)
class DbProviderRegistration:
    """Registration entry mapping database provider alias to constructor factory."""

    database_provider: str
    prepare: Callable[[DbGatewayConfiguration], DbProvider]
