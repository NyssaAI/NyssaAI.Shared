# Responsibility: Registry for database providers resolving engine factories by name.
# Public interface:
# `-- DbProviderRegistry
#     `-- register_database_providers(settings, custom_registrations)
"""Database provider registry."""

from typing import Callable, Sequence
from nyssa_ai_dbgateway.types import DbGatewayConfiguration
from nyssa_ai_dbgateway.validate_db_gateway_configuration import validate_db_gateway_configuration
from nyssa_ai_dbgateway.providers.db_provider import DbProvider
from nyssa_ai_dbgateway.providers.db_provider_registration import DbProviderRegistration
from nyssa_ai_dbgateway.providers.db_provider_sqlite import DbProviderSqlite
from nyssa_ai_dbgateway.providers.db_provider_postgres import DbProviderPostgres


class DbProviderRegistry:
    """Registry coordinating database provider factories."""

    @staticmethod
    def register_database_providers(
        settings: DbGatewayConfiguration,
        custom_registrations: Sequence[DbProviderRegistration] | None = None,
    ) -> Callable[[], DbProvider]:
        """Validate settings and return a factory producing the configured provider."""
        validate_db_gateway_configuration(settings)
        requested = settings.database_provider.strip().lower()

        factories: dict[str, Callable[[DbGatewayConfiguration], DbProvider]] = {
            "sqlite": DbProviderSqlite.prepare,
            "postgres": DbProviderPostgres.prepare,
            "postgresql": DbProviderPostgres.prepare,
            "npgsql": DbProviderPostgres.prepare,
            "psycopg": DbProviderPostgres.prepare,
        }

        if custom_registrations:
            for reg in custom_registrations:
                factories[reg.database_provider.strip().lower()] = reg.prepare

        factory = factories.get(requested)
        if factory is None:
            available = ", ".join(sorted(set(factories.keys())))
            raise ValueError(f"Unknown database provider '{settings.database_provider}'. Available providers: {available}")

        return lambda: factory(settings)
