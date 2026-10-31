# Responsibility: Use-case test verifying live SQLite operations, typed commands, and transaction atomicity.
# Public interface:
# +-- CreateItemCommand
# |   +-- bind_parameters(parameters, dialect)
# |   +-- get_sql(dialect)
# |   +-- map_result(reader)
# |   `-- new(item_id, title, active)
# +-- ItemQuery
# |   +-- bind_parameters(parameters, dialect)
# |   +-- get_sql(dialect)
# |   +-- map_row(reader)
# |   `-- new(item_id)
# +-- test_sqlite_transaction_rollback()
# `-- test_sqlite_typed_command_and_query()
"""Use-case tests for SQLite execution and transactions."""

import tempfile
import os
from typing import Mapping
from uuid import UUID, uuid4
from nyssa_ai_dbgateway.types import DbDialect, DbGatewayConfiguration
from nyssa_ai_dbgateway.commands.parameter_binder import ParameterBinder
from nyssa_ai_dbgateway.commands.data_record_reader import DataRecordReader
from nyssa_ai_dbgateway.gateway.db_gateway_client import DbGatewayClient


class CreateItemCommand:
    def __init__(self, item_id: UUID, title: str, active: bool) -> None:
        self.item_id = item_id
        self.title = title
        self.active = active

    def get_sql(self, dialect: DbDialect) -> str:
        return "INSERT INTO items (id, title, is_active) VALUES (@id, @title, @active);"

    def bind_parameters(self, parameters: ParameterBinder, dialect: DbDialect) -> None:
        parameters.add("id", self.item_id)
        parameters.add("title", self.title)
        parameters.add("active", self.active)

    def map_result(self, reader: DataRecordReader) -> int:
        return 1


class ItemQuery:
    def get_sql(self, dialect: DbDialect) -> str:
        return "SELECT id, title, is_active FROM items WHERE id = @id;"

    def bind_parameters(self, parameters: ParameterBinder, dialect: DbDialect) -> None:
        parameters.add("id", self.item_id)

    def __init__(self, item_id: UUID) -> None:
        self.item_id = item_id

    def map_row(self, reader: DataRecordReader) -> dict[str, object]:
        return {
            "id": reader.get_uuid("id"),
            "title": reader.get_string("title"),
            "is_active": reader.get_boolean("is_active"),
        }


def test_sqlite_typed_command_and_query() -> None:
    with tempfile.TemporaryDirectory() as tmp_dir:
        db_path = os.path.join(tmp_dir, "test.db")
        settings = DbGatewayConfiguration(database_provider="sqlite", connection_string=f"Data Source={db_path}")
        client = DbGatewayClient.create(settings)

        client.execute(
            "CREATE TABLE items (id TEXT PRIMARY KEY, title TEXT NOT NULL, is_active INTEGER NOT NULL);"
        )

        item_id = uuid4()
        cmd = CreateItemCommand(item_id, "Sample Item", True)
        client.execute_command(cmd)  # type: ignore[arg-type]

        query = ItemQuery(item_id)
        row = client.query_single(query)  # type: ignore[arg-type]
        assert row is not None
        assert isinstance(row, Mapping)
        assert row["id"] == item_id
        assert row["title"] == "Sample Item"
        assert row["is_active"] is True


def test_sqlite_transaction_rollback() -> None:
    with tempfile.TemporaryDirectory() as tmp_dir:
        db_path = os.path.join(tmp_dir, "test_tx.db")
        settings = DbGatewayConfiguration(database_provider="sqlite", connection_string=f"Data Source={db_path}")
        client = DbGatewayClient.create(settings)

        client.execute("CREATE TABLE counter (val INT);")
        client.execute("INSERT INTO counter VALUES (1);")

        try:
            with client.begin_transaction() as tx:
                tx.execute("UPDATE counter SET val = 2;")
                raise RuntimeError("simulated error")
        except RuntimeError:
            pass

        val = client.scalar("SELECT val FROM counter;")
        assert val == 1
