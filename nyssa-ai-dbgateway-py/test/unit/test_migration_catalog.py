# Responsibility: Unit test migration step fingerprints and catalog version resolution.
# Public interface:
# +-- test_migration_catalog_resolution()
# `-- test_migration_step_fingerprint_deterministic_and_matches_wire_formula()
"""Unit tests for migrations catalog and fingerprint calculation."""

from nyssa_ai_dbgateway.migrations.migration_step import MigrationStep
from nyssa_ai_dbgateway.migrations.migration_release_pin import MigrationReleasePin
from nyssa_ai_dbgateway.migrations.migration_catalog import MigrationCatalog


def test_migration_step_fingerprint_deterministic_and_matches_wire_formula() -> None:
    # "{number}:{len(up)}:{up}:{down_len}:{down or ''}"
    step = MigrationStep.universal(number=1, up_sql="CREATE TABLE test (id INT);", down_sql="DROP TABLE test;")
    fp = step.compute_fingerprint()
    assert len(fp) == 64
    assert fp.islower()
    # Confirm deterministic
    assert fp == step.compute_fingerprint()


def test_migration_catalog_resolution() -> None:
    steps = [
        MigrationStep.universal(1, "SELECT 1;", "SELECT 0;"),
        MigrationStep.universal(2, "SELECT 2;", "SELECT 1;"),
    ]
    releases = [
        MigrationReleasePin("1.0.0", 1),
        MigrationReleasePin("1.1.0", 2),
    ]
    catalog = MigrationCatalog(steps, releases)
    assert catalog.latest_step_number == 2
    assert catalog.resolve_target_version("1.0.0") == 1
    assert catalog.resolve_target_version("1.1.0") == 2
    assert catalog.resolve_target_version(2) == 2
    assert catalog.resolve_target_version("2") == 2
