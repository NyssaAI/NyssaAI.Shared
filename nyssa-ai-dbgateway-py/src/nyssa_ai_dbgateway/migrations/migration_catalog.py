# Responsibility: Manage an ordered catalog of migration steps and release pins.
# Public interface:
# `-- MigrationCatalog
#     +-- create(steps, releases)
#     +-- fingerprint(step, dialect)
#     +-- get_step(number)
#     +-- latest_step_number
#     +-- new(steps, releases)
#     +-- releases
#     +-- resolve(release)
#     +-- resolve_target_version(target)
#     `-- steps
"""Migration catalog."""

import re
from typing import Sequence
from nyssa_ai_dbgateway.types import DbDialect
from nyssa_ai_dbgateway.migrations.migration_step import MigrationStep
from nyssa_ai_dbgateway.migrations.migration_release_pin import MigrationReleasePin
from nyssa_ai_dbgateway.migrations.migration_sql import MigrationSql

_SEMVER_PATTERN = re.compile(
    r"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)"
    r"(?:-((?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*)"
    r"(?:\.(?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*))*))?"
    r"(?:\+([0-9a-zA-Z-]+(?:\.[0-9a-zA-Z-]+)*))?$"
)


class MigrationCatalog:
    """Stores and validates ordered migration steps and release aliases."""

    def __init__(
        self,
        steps: Sequence[MigrationStep],
        releases: Sequence[MigrationReleasePin] | None = None,
    ) -> None:
        self._steps = sorted(steps, key=lambda s: s.number)
        self._releases = list(releases or [])

        # Validate step numbering (must start at 1 and be contiguous) and transactional SQL
        for i, step in enumerate(self._steps):
            expected = i + 1
            if step.number != expected:
                raise ValueError(
                    f"Migration steps must be contiguous starting at 1. Expected {expected}, got {step.number}."
                )
            self._validate_step_sql(step)

        # Validate release pins when provided
        seen_releases: set[str] = set()
        for pin in self._releases:
            if not pin.release or not _SEMVER_PATTERN.match(pin.release):
                raise ValueError("Migration release pins must be exact full SemVer strings.")
            if pin.number < 0 or pin.number > len(self._steps):
                raise ValueError("Migration release pins must target an existing step or 0.")
            if pin.release in seen_releases:
                raise ValueError(f"Duplicate migration release pin: {pin.release}")
            seen_releases.add(pin.release)

    @staticmethod
    def _validate_step_sql(step: MigrationStep) -> None:
        if step.up_sql is not None:
            if not MigrationSql.is_transactional(step.up_sql):
                raise ValueError("Migration SQL must contain valid transactional statements.")
        else:
            if not MigrationSql.is_transactional(step.sqlite_up_sql) or not MigrationSql.is_transactional(
                step.postgres_up_sql
            ):
                raise ValueError("Migration SQL must contain valid transactional statements.")

        if step.down_sql is not None and not MigrationSql.is_transactional(step.down_sql):
            raise ValueError("Migration SQL must contain valid transactional statements.")
        if step.sqlite_down_sql is not None and not MigrationSql.is_transactional(step.sqlite_down_sql):
            raise ValueError("Migration SQL must contain valid transactional statements.")
        if step.postgres_down_sql is not None and not MigrationSql.is_transactional(step.postgres_down_sql):
            raise ValueError("Migration SQL must contain valid transactional statements.")

    @property
    def steps(self) -> Sequence[MigrationStep]:
        return self._steps

    @property
    def releases(self) -> Sequence[MigrationReleasePin]:
        return self._releases

    @property
    def latest_step_number(self) -> int:
        return self._steps[-1].number if self._steps else 0

    def get_step(self, number: int) -> MigrationStep | None:
        for s in self._steps:
            if s.number == number:
                return s
        return None

    def resolve(self, release: str) -> int:
        if not release or not release.strip():
            raise ValueError("Release identifier must be a non-empty string.")
        trimmed = release.strip()
        for rel in self._releases:
            if rel.release == trimmed:
                return rel.number
        raise ValueError(f"The requested migration release '{release}' is not registered.")

    def resolve_target_version(self, target: str | int) -> int:
        if isinstance(target, int):
            return target
        trimmed = target.strip()
        if trimmed.isdigit():
            return int(trimmed)
        for rel in self._releases:
            if rel.release == trimmed:
                return rel.number
        raise ValueError(f"Unknown target migration release or version: '{target}'.")

    @staticmethod
    def fingerprint(step: MigrationStep, dialect: DbDialect) -> str:
        return step.compute_fingerprint(dialect)

    @staticmethod
    def create(
        steps: Sequence[MigrationStep],
        releases: Sequence[MigrationReleasePin] | None = None,
    ) -> "MigrationCatalog":
        return MigrationCatalog(steps, releases)
