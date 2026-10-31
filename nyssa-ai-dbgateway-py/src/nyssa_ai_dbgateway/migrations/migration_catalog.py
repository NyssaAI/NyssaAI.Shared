# Responsibility: Manage an ordered catalog of migration steps and release pins.
# Public interface:
# `-- MigrationCatalog
#     +-- create(steps, releases)
#     +-- get_step(number)
#     +-- latest_step_number
#     +-- new(steps, releases)
#     +-- releases
#     +-- resolve_target_version(target)
#     `-- steps
"""Migration catalog."""

from typing import Sequence
from nyssa_ai_dbgateway.migrations.migration_step import MigrationStep
from nyssa_ai_dbgateway.migrations.migration_release_pin import MigrationReleasePin


class MigrationCatalog:
    """Stores and validates ordered migration steps and release aliases."""

    def __init__(
        self,
        steps: Sequence[MigrationStep],
        releases: Sequence[MigrationReleasePin] | None = None,
    ) -> None:
        self._steps = sorted(steps, key=lambda s: s.number)
        self._releases = list(releases or [])

        # Validate step numbering (must start at 1 and be contiguous)
        for i, step in enumerate(self._steps):
            expected = i + 1
            if step.number != expected:
                raise ValueError(f"Migration steps must be sequential starting at 1. Expected {expected}, got {step.number}.")

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
    def create(
        steps: Sequence[MigrationStep],
        releases: Sequence[MigrationReleasePin] | None = None,
    ) -> "MigrationCatalog":
        return MigrationCatalog(steps, releases)
