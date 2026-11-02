# Responsibility: Use-case test verifying migration up and rollback cycles.
# Public interface:
# +-- test_migration_corrupted_fingerprint_raises_value_error_on_verify_and_migrate()
# +-- test_migration_paired_and_universal_steps_verify_idempotent_rerun_and_downgrade_to_zero()
# `-- test_sqlite_migration_upgrade_and_downgrade()
"""Use-case tests for database schema migrations."""

import os
import tempfile
import pytest
from nyssa_ai_dbgateway.types import DbGatewayConfiguration
from nyssa_ai_dbgateway.gateway.db_gateway_client import DbGatewayClient
from nyssa_ai_dbgateway.migrations.migration_step import MigrationStep
from nyssa_ai_dbgateway.migrations.migration_release_pin import MigrationReleasePin
from nyssa_ai_dbgateway.migrations.migration_catalog import MigrationCatalog
from nyssa_ai_dbgateway.migrations.migration_runner import MigrationRunner
from nyssa_ai_dbgateway.migrations.migrations_table_name import MIGRATIONS_TABLE_NAME


def test_sqlite_migration_upgrade_and_downgrade() -> None:
    with tempfile.TemporaryDirectory() as tmp_dir:
        db_path = os.path.join(tmp_dir, "mig_test.db")
        settings = DbGatewayConfiguration(database_provider="sqlite", connection_string=f"Data Source={db_path}")
        client = DbGatewayClient.create(settings)

        catalog = MigrationCatalog(
            steps=[
                MigrationStep.universal(
                    1,
                    up_sql="CREATE TABLE items (id TEXT PRIMARY KEY, title TEXT NOT NULL);",
                    down_sql="DROP TABLE items;",
                ),
                MigrationStep.universal(
                    2,
                    up_sql="ALTER TABLE items ADD COLUMN note TEXT;",
                    down_sql="ALTER TABLE items DROP COLUMN note;",
                ),
            ],
            releases=[
                MigrationReleasePin("1.0.0", 1),
                MigrationReleasePin("1.1.0", 2),
            ],
        )

        runner = MigrationRunner(client._provider, catalog)

        # Migrate up
        report = runner.migrate_up()
        assert report.current_version == 2
        assert runner.get_current_version() == 2
        runner.verify()

        # Check column exists
        client.execute("INSERT INTO items (id, title, note) VALUES ('1', 'T1', 'N1');")

        # Downgrade to 1.0.0
        downgrade_report = runner.migrate("1.0.0")
        assert downgrade_report.current_version == 1
        assert runner.get_current_version() == 1


def test_migration_paired_and_universal_steps_verify_idempotent_rerun_and_downgrade_to_zero() -> None:
    with tempfile.TemporaryDirectory() as tmp_dir:
        db_path = os.path.join(tmp_dir, "mig_lifecycle.db")
        settings = DbGatewayConfiguration(database_provider="sqlite", connection_string=f"Data Source={db_path}")
        client = DbGatewayClient.create(settings)

        catalog = MigrationCatalog.create(
            steps=[
                MigrationStep.paired(
                    1,
                    sqlite_up_sql="CREATE TABLE items (id TEXT PRIMARY KEY, active INTEGER NOT NULL);",
                    postgres_up_sql="CREATE TABLE items (id UUID PRIMARY KEY, active BOOLEAN NOT NULL);",
                    sqlite_down_sql="DROP TABLE items;",
                    postgres_down_sql="DROP TABLE items;",
                ),
                MigrationStep.universal(
                    2,
                    up_sql="ALTER TABLE items ADD COLUMN note TEXT;",
                    down_sql="ALTER TABLE items DROP COLUMN note;",
                ),
            ],
            releases=[
                MigrationReleasePin("0.0.0", 0),
                MigrationReleasePin("1.0.0", 1),
                MigrationReleasePin("1.1.0", 2),
            ],
        )

        runner = MigrationRunner(client._provider, catalog)
        assert runner.get_current_version() == 0

        # 1. Migrate to 1.0.0
        report1 = runner.migrate("1.0.0")
        assert report1.initial_version == 0
        assert report1.current_version == 1
        assert list(report1.applied_steps) == [1]

        history = runner.get_history()
        assert len(history) == 1
        assert history[0][0] == 1
        assert history[0][1] == MigrationCatalog.fingerprint(catalog.steps[0], "sqlite")

        # 2. Migrate to 1.1.0
        report2 = runner.migrate("1.1.0")
        assert report2.initial_version == 1
        assert report2.current_version == 2
        assert list(report2.applied_steps) == [2]
        runner.verify()

        client.execute("INSERT INTO items (id, active, note) VALUES ('item-1', 1, 'hello');")

        # 3. Idempotent re-run at 1.1.0
        report_idempotent = runner.migrate("1.1.0")
        assert report_idempotent.initial_version == 2
        assert report_idempotent.current_version == 2
        assert list(report_idempotent.applied_steps) == []
        assert list(report_idempotent.reverted_steps) == []

        # 4. Downgrade to 1.0.0 preserves step-1 table data
        report_down_1 = runner.migrate("1.0.0")
        assert report_down_1.initial_version == 2
        assert report_down_1.current_version == 1
        assert list(report_down_1.reverted_steps) == [2]
        assert client.scalar("SELECT COUNT(*) FROM items WHERE id = 'item-1';") == 1

        # 5. Downgrade to 0.0.0 reverts to version 0
        report_down_0 = runner.migrate("0.0.0")
        assert report_down_0.initial_version == 1
        assert report_down_0.current_version == 0
        assert list(report_down_0.reverted_steps) == [1]
        assert runner.get_current_version() == 0


def test_migration_corrupted_fingerprint_raises_value_error_on_verify_and_migrate() -> None:
    with tempfile.TemporaryDirectory() as tmp_dir:
        db_path = os.path.join(tmp_dir, "mig_corrupt.db")
        settings = DbGatewayConfiguration(database_provider="sqlite", connection_string=f"Data Source={db_path}")
        client = DbGatewayClient.create(settings)

        catalog = MigrationCatalog.create(
            steps=[
                MigrationStep.universal(
                    1,
                    up_sql="CREATE TABLE items (id TEXT PRIMARY KEY);",
                    down_sql="DROP TABLE items;",
                ),
            ],
            releases=[MigrationReleasePin("1.0.0", 1)],
        )
        runner = MigrationRunner(client._provider, catalog)
        runner.migrate_up()
        runner.verify()

        client.execute(
            f"UPDATE {MIGRATIONS_TABLE_NAME} SET fingerprint = '0000' WHERE number = 1;"
        )

        with pytest.raises(ValueError, match="fingerprint mismatch"):
            runner.verify()

        with pytest.raises(ValueError, match="Corrupted migration history"):
            runner.migrate_up()
