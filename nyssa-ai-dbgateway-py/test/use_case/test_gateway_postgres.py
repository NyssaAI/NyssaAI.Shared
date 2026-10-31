# Responsibility: Use-case test verifying PostgreSQL execution when docker container is available.
# Public interface:
# +-- get_postgres_connection_string()
# `-- test_postgres_ad_hoc_and_transactions()
"""Use-case tests for PostgreSQL."""

import os
import pytest
from nyssa_ai_dbgateway.types import DbGatewayConfiguration
from nyssa_ai_dbgateway.gateway.db_gateway_client import DbGatewayClient


def get_postgres_connection_string() -> str | None:
    return os.environ.get("NYSSAAI_DBGATEWAY_POSTGRES") or os.environ.get("NYSSAAI_REPOSITORY_POSTGRES")


def test_postgres_ad_hoc_and_transactions() -> None:
    conn_str = get_postgres_connection_string()
    if not conn_str:
        pytest.skip("PostgreSQL test database not configured in environment.")

    settings = DbGatewayConfiguration(database_provider="postgresql", connection_string=conn_str)
    client = DbGatewayClient.create(settings)

    client.execute("DROP TABLE IF EXISTS py_test_items;")
    client.execute("CREATE TABLE py_test_items (id SERIAL PRIMARY KEY, title TEXT NOT NULL);")

    # Insert ad-hoc
    client.execute("INSERT INTO py_test_items (title) VALUES (@title);", {"title": "PG Item"})

    # Query
    rows = client.query("SELECT id, title FROM py_test_items WHERE title = @title;", {"title": "PG Item"})
    assert len(rows) == 1

    # Clean up
    client.execute("DROP TABLE py_test_items;")
