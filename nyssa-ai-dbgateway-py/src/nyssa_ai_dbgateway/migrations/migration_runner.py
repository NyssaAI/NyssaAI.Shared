# Responsibility: Concurrency-safe migration runner applying and reverting versioned catalog steps.
# Public interface:
# `-- MigrationRunner
#     +-- get_current_version()
#     +-- get_history()
#     +-- migrate(target)
#     +-- migrate_up()
#     +-- new(provider, catalog, logger)
#     `-- verify(strict)
"""Migration runner executing versioned database migrations."""

from nyssa_ai_dbgateway.providers.db_provider import DbProvider
from nyssa_ai_dbgateway.migrations.migration_catalog import MigrationCatalog
from nyssa_ai_dbgateway.migrations.migration_report import MigrationReport
from nyssa_ai_dbgateway.migrations.migrations_table_name import (
    MIGRATIONS_TABLE_NAME,
    SQL_TABLE_SQLITE,
    SQL_TABLE_POSTGRES,
    SQL_SELECT_HISTORY,
    SQL_LOCK_POSTGRES,
    SQL_UNLOCK_POSTGRES,
)
from nyssa_ai_dbgateway.logging.db_gateway_logger import DbGatewayLogger


class MigrationRunner:
    """Orchestrates schema migrations with fingerprint verification and concurrency locking."""

    def __init__(
        self,
        provider: DbProvider,
        catalog: MigrationCatalog,
        logger: DbGatewayLogger | None = None,
    ) -> None:
        self._provider = provider
        self._catalog = catalog
        self._logger = logger

    def get_history(self) -> list[tuple[int, str]]:
        """Return list of (number, fingerprint) applied so far."""
        conn = self._provider.open_connection()
        try:
            self._ensure_table(conn)
            _, rows = conn.execute_query(SQL_SELECT_HISTORY)
            history: list[tuple[int, str]] = []
            for r in rows:
                num_val = r[0]
                num = int(num_val) if isinstance(num_val, (int, str)) else 0
                history.append((num, str(r[1])))
            return history
        finally:
            conn.close()

    def get_current_version(self) -> int:
        """Return latest applied migration number."""
        history = self.get_history()
        return history[-1][0] if history else 0

    def verify(self, strict: bool = True) -> None:
        """Verify that existing database migration history matches current catalog fingerprints."""
        history = self.get_history()
        for num, fp in history:
            step = self._catalog.get_step(num)
            if step is None:
                if strict:
                    raise ValueError(f"Applied migration step {num} is missing from catalog.")
                continue
            expected_fp = step.compute_fingerprint()
            if fp != expected_fp:
                raise ValueError(
                    f"Migration step {num} fingerprint mismatch. Database: {fp}, Catalog: {expected_fp}."
                )

    def migrate_up(self) -> MigrationReport:
        """Migrate up to the catalog's latest step."""
        return self.migrate(self._catalog.latest_step_number)

    def migrate(self, target: str | int) -> MigrationReport:
        """Migrate database to target step number or release tag."""
        target_version = self._catalog.resolve_target_version(target)
        if target_version < 0 or target_version > self._catalog.latest_step_number:
            raise ValueError(f"Target version {target_version} is out of bounds (0..{self._catalog.latest_step_number}).")

        conn = self._provider.open_connection()
        dialect = self._provider.dialect

        try:
            if dialect == "postgres":
                conn.execute_non_query(SQL_LOCK_POSTGRES)
            self._ensure_table(conn)

            history = self.get_history()
            initial_version = history[-1][0] if history else 0

            # Verify integrity
            for num, fp in history:
                step = self._catalog.get_step(num)
                if step is not None and step.compute_fingerprint() != fp:
                    raise ValueError(f"Corrupted migration history on step {num}.")

            applied: list[int] = []
            reverted: list[int] = []
            current = initial_version

            if target_version > initial_version:
                # Upgrade
                for step_num in range(initial_version + 1, target_version + 1):
                    step = self._catalog.get_step(step_num)
                    if step is None:
                        raise ValueError(f"Missing migration step {step_num}.")
                    up_sql = step.get_up_sql(dialect)
                    fp = step.compute_fingerprint()

                    conn.begin_transaction()
                    try:
                        conn.execute_non_query(up_sql)
                        insert_sql = (
                            f"INSERT INTO {MIGRATIONS_TABLE_NAME} (number, fingerprint) VALUES (@p0, @p1);"
                            if dialect == "sqlite"
                            else f"INSERT INTO {MIGRATIONS_TABLE_NAME} (number, fingerprint) VALUES ($1, $2);"
                        )
                        params = {"p0": step_num, "p1": fp} if dialect == "sqlite" else [step_num, fp]
                        conn.execute_non_query(insert_sql, params)
                        conn.commit()
                        applied.append(step_num)
                        current = step_num
                    except Exception:
                        conn.rollback()
                        raise
            elif target_version < initial_version:
                # Downgrade
                for step_num in range(initial_version, target_version, -1):
                    step = self._catalog.get_step(step_num)
                    if step is None:
                        raise ValueError(f"Missing migration step {step_num} for rollback.")
                    down_sql = step.get_down_sql(dialect)
                    if down_sql is None:
                        raise ValueError(f"Migration step {step_num} does not define rollback down_sql.")

                    conn.begin_transaction()
                    try:
                        conn.execute_non_query(down_sql)
                        delete_sql = (
                            f"DELETE FROM {MIGRATIONS_TABLE_NAME} WHERE number = @p0;"
                            if dialect == "sqlite"
                            else f"DELETE FROM {MIGRATIONS_TABLE_NAME} WHERE number = $1;"
                        )
                        params = {"p0": step_num} if dialect == "sqlite" else [step_num]
                        conn.execute_non_query(delete_sql, params)
                        conn.commit()
                        reverted.append(step_num)
                        current = step_num - 1
                    except Exception:
                        conn.rollback()
                        raise

            return MigrationReport(
                initial_version=initial_version,
                current_version=current,
                applied_steps=applied,
                reverted_steps=reverted,
                target_version=target_version,
            )
        finally:
            if dialect == "postgres":
                try:
                    conn.execute_non_query(SQL_UNLOCK_POSTGRES)
                except Exception:
                    pass
            conn.close()

    def _ensure_table(self, conn: object) -> None:
        table_sql = SQL_TABLE_SQLITE if self._provider.dialect == "sqlite" else SQL_TABLE_POSTGRES
        conn.execute_non_query(table_sql)  # type: ignore[attr-defined]
