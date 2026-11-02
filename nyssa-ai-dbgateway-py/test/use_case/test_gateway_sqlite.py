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
# +-- test_sqlite_ad_hoc_execute_scalar_query_single_and_query()
# +-- test_sqlite_begin_transaction_latches_failure_and_blocks_subsequent_commands_and_commit()
# +-- test_sqlite_execute_transaction_commits_on_success_and_rolls_back_on_error()
# +-- test_sqlite_transaction_rollback()
# `-- test_sqlite_typed_command_and_query()
"""Use-case tests for SQLite execution and transactions."""

import os
import tempfile
from typing import Mapping
from uuid import UUID, uuid4
import pytest
from nyssa_ai_dbgateway.types import DbDialect, DbGatewayConfiguration
from nyssa_ai_dbgateway.commands.parameter_binder import ParameterBinder
from nyssa_ai_dbgateway.commands.data_record_reader import DataRecordReader
from nyssa_ai_dbgateway.gateway.db_gateway_client import DbGatewayClient
from nyssa_ai_dbgateway.db_transaction import DbTransaction


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


def test_sqlite_ad_hoc_execute_scalar_query_single_and_query() -> None:
    with tempfile.TemporaryDirectory() as tmp_dir:
        db_path = os.path.join(tmp_dir, "test_adhoc.db")
        settings = DbGatewayConfiguration(database_provider="sqlite", connection_string=f"Data Source={db_path}")
        client = DbGatewayClient.create(settings)

        client.execute(
            "CREATE TABLE tasks (id TEXT PRIMARY KEY, title TEXT NOT NULL, is_completed INTEGER NOT NULL);"
        )

        inserted = client.execute(
            "INSERT INTO tasks (id, title, is_completed) VALUES (@id, @title, @is_completed);",
            {"id": "task-1", "title": "First Task", "is_completed": True},
        )
        assert inserted == 1

        count = client.scalar("SELECT COUNT(*) FROM tasks;")
        assert count == 1

        single = client.query_single(
            "SELECT id, title FROM tasks WHERE id = @id;",
            {"id": "task-1"},
        )
        assert single == {"id": "task-1", "title": "First Task"}

        all_rows = client.query("SELECT id, title FROM tasks;")
        assert len(all_rows) == 1
        assert all_rows[0] == {"id": "task-1", "title": "First Task"}


def test_sqlite_execute_transaction_commits_on_success_and_rolls_back_on_error() -> None:
    with tempfile.TemporaryDirectory() as tmp_dir:
        db_path = os.path.join(tmp_dir, "test_exec_tx.db")
        settings = DbGatewayConfiguration(database_provider="sqlite", connection_string=f"Data Source={db_path}")
        client = DbGatewayClient.create(settings)

        client.execute(
            "CREATE TABLE tasks (id TEXT PRIMARY KEY, title TEXT NOT NULL, is_completed INTEGER NOT NULL);"
        )

        def commit_both(tx: DbTransaction) -> None:
            tx.execute(
                "INSERT INTO tasks (id, title, is_completed) VALUES (@id, @title, @isCompleted);",
                {"id": "tx-task-1", "title": "Tx Task 1", "isCompleted": False},
            )
            tx.execute(
                "INSERT INTO tasks (id, title, is_completed) VALUES (@id, @title, @isCompleted);",
                {"id": "tx-task-2", "title": "Tx Task 2", "isCompleted": True},
            )

        client.execute_transaction(commit_both)
        assert client.scalar("SELECT COUNT(*) FROM tasks;") == 2

        def fail_second(tx: DbTransaction) -> None:
            tx.execute(
                "INSERT INTO tasks (id, title, is_completed) VALUES (@id, @title, @isCompleted);",
                {"id": "tx-task-3", "title": "Tx Task 3", "isCompleted": False},
            )
            raise RuntimeError("Simulated transactional failure!")

        with pytest.raises(RuntimeError, match="Simulated transactional failure!"):
            client.execute_transaction(fail_second)

        assert client.scalar("SELECT COUNT(*) FROM tasks;") == 2


def test_sqlite_begin_transaction_latches_failure_and_blocks_subsequent_commands_and_commit() -> None:
    with tempfile.TemporaryDirectory() as tmp_dir:
        db_path = os.path.join(tmp_dir, "test_latch.db")
        settings = DbGatewayConfiguration(database_provider="sqlite", connection_string=f"Data Source={db_path}")
        client = DbGatewayClient.create(settings)

        client.execute(
            "CREATE TABLE tasks (id TEXT PRIMARY KEY, title TEXT NOT NULL, is_completed INTEGER NOT NULL);"
        )

        with client.begin_transaction() as tx:
            tx.execute(
                "INSERT INTO tasks (id, title, is_completed) VALUES (@id, @title, 0);",
                {"id": "pre-fail", "title": "Should Roll Back"},
            )
            with pytest.raises(Exception):
                tx.execute("INSERT INTO non_existent_table VALUES (1);")

            with pytest.raises(RuntimeError, match="The transaction failed and must be rolled back."):
                tx.execute(
                    "INSERT INTO tasks (id, title, is_completed) VALUES (@id, @title, 0);",
                    {"id": "latch-1", "title": "Should Fail"},
                )

            with pytest.raises(RuntimeError, match="The transaction failed and must be rolled back."):
                tx.commit()

        assert client.scalar("SELECT COUNT(*) FROM tasks;") == 0


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
