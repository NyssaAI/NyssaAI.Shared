# Responsibility: Use-case test verifying migration up and rollback cycles.
# Public interface:
# `-- test_sqlite_migration_upgrade_and_downgrade()
"""Use-case tests for database schema migrations."""

import tempfile
import os
from nyssa_ai_dbgateway.types import DbGatewayConfiguration
from nyssa_ai_dbgateway.gateway.db_gateway_client import DbGatewayClient
from nyssa_ai_dbgateway.migrations.migration_step import MigrationStep
from nyssa_ai_dbgateway.migrations.migration_release_pin import MigrationReleasePin
from nyssa_ai_dbgateway.migrations.migration_catalog import MigrationCatalog
from nyssa_ai_dbgateway.migrations.migration_runner import MigrationRunner


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
