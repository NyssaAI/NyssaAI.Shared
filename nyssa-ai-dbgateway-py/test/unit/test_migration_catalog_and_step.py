# Responsibility: Unit test migration step creation, catalog validation, and SHA-256 fingerprinting.
# Public interface:
# +-- test_migration_catalog_create_validates_contiguous_steps_and_semver_pins()
# +-- test_migration_catalog_fingerprint_matches_formula_identically()
# +-- test_migration_catalog_invalid_pins_or_steps_raises_errors()
# `-- test_migration_step_universal_and_paired_returns_dialect_specific_sql()
"""Unit tests for MigrationCatalog and MigrationStep."""

import pytest
from nyssa_ai_dbgateway.migrations.migration_step import MigrationStep
from nyssa_ai_dbgateway.migrations.migration_release_pin import MigrationReleasePin
from nyssa_ai_dbgateway.migrations.migration_catalog import MigrationCatalog


def test_migration_step_universal_and_paired_returns_dialect_specific_sql() -> None:
    universal = MigrationStep.universal(
        1,
        "CREATE TABLE universal_items (id TEXT PRIMARY KEY);",
        "DROP TABLE universal_items;",
    )
    assert universal.get_up_sql("sqlite") == universal.get_up_sql("postgres")
    assert universal.get_down_sql("sqlite") == universal.get_down_sql("postgres")

    paired = MigrationStep.paired(
        2,
        "CREATE TABLE paired_items (id TEXT PRIMARY KEY);",
        "CREATE TABLE paired_items (id UUID PRIMARY KEY);",
    )
    assert paired.get_up_sql("sqlite") == "CREATE TABLE paired_items (id TEXT PRIMARY KEY);"
    assert paired.get_up_sql("postgres") == "CREATE TABLE paired_items (id UUID PRIMARY KEY);"
    assert paired.get_down_sql("sqlite") is None


def test_migration_catalog_fingerprint_matches_formula_identically() -> None:
    step = MigrationStep.paired(
        1,
        "CREATE TABLE items (id TEXT PRIMARY KEY, active INTEGER NOT NULL)",
        "CREATE TABLE items (id UUID PRIMARY KEY, active BOOLEAN NOT NULL)",
        "DROP TABLE items",
        "DROP TABLE items",
    )

    sqlite_fingerprint = MigrationCatalog.fingerprint(step, "sqlite")
    assert sqlite_fingerprint == "a53e10b4c4dccd8f675475310dffabc2e66d5f805885948b015c5257cf5fabe1"
    assert len(sqlite_fingerprint) == 64


def test_migration_catalog_create_validates_contiguous_steps_and_semver_pins() -> None:
    catalog = MigrationCatalog.create(
        [
            MigrationStep.universal(1, "CREATE TABLE a (id INT);"),
            MigrationStep.universal(2, "CREATE TABLE b (id INT);"),
        ],
        [
            MigrationReleasePin("1.0.0", 1),
            MigrationReleasePin("1.1.0", 2),
            MigrationReleasePin("0.0.0", 0),
        ],
    )

    assert len(catalog.steps) == 2
    assert catalog.resolve("1.0.0") == 1
    assert catalog.resolve("1.1.0") == 2
    assert catalog.resolve("0.0.0") == 0
    with pytest.raises(ValueError):
        catalog.resolve("2.0.0")


def test_migration_catalog_invalid_pins_or_steps_raises_errors() -> None:
    # Non-contiguous steps
    with pytest.raises(ValueError, match="Migration steps must be contiguous starting at 1"):
        MigrationCatalog.create(
            [MigrationStep.universal(2, "CREATE TABLE a (id INT);")],
            [MigrationReleasePin("1.0.0", 2)],
        )

    # Invalid SemVer pin
    with pytest.raises(ValueError, match="Migration release pins must be exact full SemVer strings"):
        MigrationCatalog.create(
            [MigrationStep.universal(1, "CREATE TABLE a (id INT);")],
            [MigrationReleasePin("v1.0", 1)],
        )

    # Step out of bounds
    with pytest.raises(ValueError, match="Migration release pins must target an existing step or 0"):
        MigrationCatalog.create(
            [MigrationStep.universal(1, "CREATE TABLE a (id INT);")],
            [MigrationReleasePin("1.0.0", 99)],
        )

    # Duplicate release pin
    with pytest.raises(ValueError, match="Duplicate migration release pin"):
        MigrationCatalog.create(
            [MigrationStep.universal(1, "CREATE TABLE a (id INT);")],
            [MigrationReleasePin("1.0.0", 1), MigrationReleasePin("1.0.0", 0)],
        )

    # Non-transactional SQL inside step
    with pytest.raises(ValueError, match="Migration SQL must contain valid transactional statements"):
        MigrationCatalog.create(
            [MigrationStep.universal(1, "BEGIN TRANSACTION; CREATE TABLE a (id INT); COMMIT;")],
            [MigrationReleasePin("1.0.0", 1)],
        )
