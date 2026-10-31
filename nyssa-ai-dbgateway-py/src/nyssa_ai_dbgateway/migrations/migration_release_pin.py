# Responsibility: Pin a semver release tag to a migration step number.
# Public interface:
# `-- MigrationReleasePin
#     +-- number
#     `-- release
"""Migration release pin DTO."""

from dataclasses import dataclass


@dataclass(frozen=True, slots=True)
class MigrationReleasePin:
    """Pins a semantic release tag to a migration step version number."""

    release: str
    number: int
