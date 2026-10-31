# Responsibility: PostgreSQL database provider implementation.
# Public interface:
# `-- DbProviderPostgres
#     +-- database_provider
#     +-- dialect
#     +-- new(connection_string)
#     +-- open_connection()
#     `-- prepare(settings)
"""PostgreSQL database provider."""

import psycopg
from nyssa_ai_dbgateway.types import DbDialect, DbGatewayConfiguration
from nyssa_ai_dbgateway.providers.db_connection_adapter import DbConnectionAdapter
from nyssa_ai_dbgateway.providers.postgres_connection_adapter import PostgresConnectionAdapter


class DbProviderPostgres:
    """Database provider for PostgreSQL using psycopg."""

    def __init__(self, connection_string: str) -> None:
        self._connection_string = connection_string

    @property
    def database_provider(self) -> str:
        return "postgresql"

    @property
    def dialect(self) -> DbDialect:
        return "postgres"

    def open_connection(self) -> DbConnectionAdapter:
        conn = psycopg.connect(self._connection_string, autocommit=False)
        return PostgresConnectionAdapter(conn)

    @staticmethod
    def prepare(settings: DbGatewayConfiguration) -> "DbProviderPostgres":
        return DbProviderPostgres(settings.connection_string)
