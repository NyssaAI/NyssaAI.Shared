# Responsibility: File-system migration catalog loader discovering universal and paired step files.
# Public interface:
# +-- STEP_FILE_REGEX
# `-- load_migration_catalog(directory)
"""Migration catalog loader reading SQL files and lockpins from disk."""

import os
import re
from nyssa_ai_dbgateway.migrations.migration_step import MigrationStep
from nyssa_ai_dbgateway.migrations.migration_release_pin import MigrationReleasePin
from nyssa_ai_dbgateway.migrations.migration_catalog import MigrationCatalog

STEP_FILE_REGEX = re.compile(
    r"^(\d+)_(.+?)(?:\.(sqlite|postgres))?\.(up|down)\.sql$",
    re.IGNORECASE,
)


def load_migration_catalog(directory: str) -> MigrationCatalog:
    """Load SQL migration steps and release pins from a folder."""
    if not os.path.exists(directory):
        raise ValueError(f"Migration directory does not exist: {directory}")

    steps_by_num: dict[int, dict[str, str]] = {}
    releases: list[MigrationReleasePin] = []

    for filename in sorted(os.listdir(directory)):
        full_path = os.path.join(directory, filename)
        if os.path.isdir(full_path):
            continue

        if filename.lower() in ("migrations.lock", "release.lock"):
            with open(full_path, "r", encoding="utf-8") as f:
                for line in f:
                    stripped = line.strip()
                    if not stripped or stripped.startswith("#"):
                        continue
                    parts = stripped.split("=")
                    if len(parts) == 2:
                        releases.append(MigrationReleasePin(release=parts[0].strip(), number=int(parts[1].strip())))
            continue

        match = STEP_FILE_REGEX.match(filename)
        if match:
            num = int(match.group(1))
            engine = (match.group(3) or "universal").lower()
            direction = match.group(4).lower()

            with open(full_path, "r", encoding="utf-8") as f:
                content = f.read()

            if num not in steps_by_num:
                steps_by_num[num] = {}
            steps_by_num[num][f"{engine}_{direction}"] = content

    compiled_steps: list[MigrationStep] = []
    for num in sorted(steps_by_num.keys()):
        data = steps_by_num[num]
        if "universal_up" in data:
            compiled_steps.append(
                MigrationStep.universal(
                    number=num,
                    up_sql=data["universal_up"],
                    down_sql=data.get("universal_down"),
                )
            )
        else:
            compiled_steps.append(
                MigrationStep.paired(
                    number=num,
                    sqlite_up_sql=data.get("sqlite_up", ""),
                    postgres_up_sql=data.get("postgres_up", ""),
                    sqlite_down_sql=data.get("sqlite_down"),
                    postgres_down_sql=data.get("postgres_down"),
                )
            )

    return MigrationCatalog(compiled_steps, releases)
