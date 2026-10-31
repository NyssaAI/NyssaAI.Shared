# Responsibility: Report results and applied/reverted step details from a migration run.
# Public interface:
# `-- MigrationReport
#     +-- applied_steps
#     +-- current_version
#     +-- initial_version
#     +-- reverted_steps
#     `-- target_version
"""Migration execution report DTO."""

from dataclasses import dataclass
from typing import Sequence


@dataclass(frozen=True, slots=True)
class MigrationReport:
    """Summary of executed migration lifecycle operations."""

    initial_version: int
    current_version: int
    applied_steps: Sequence[int]
    reverted_steps: Sequence[int]
    target_version: int
