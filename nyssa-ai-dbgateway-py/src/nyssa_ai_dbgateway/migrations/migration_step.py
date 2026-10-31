# Responsibility: Define migration step record with universal or dialect-specific SQL and SHA-256 fingerprint.
# Public interface:
# `-- MigrationStep
#     +-- compute_fingerprint()
#     +-- down_sql
#     +-- get_down_sql(dialect)
#     +-- get_up_sql(dialect)
#     +-- number
#     +-- paired(number, sqlite_up_sql, postgres_up_sql, sqlite_down_sql, postgres_down_sql)
#     +-- postgres_down_sql
#     +-- postgres_up_sql
#     +-- sqlite_down_sql
#     +-- sqlite_up_sql
#     +-- universal(number, up_sql, down_sql)
#     `-- up_sql
"""Migration step DTO."""

import hashlib
from dataclasses import dataclass
from nyssa_ai_dbgateway.types import DbDialect


@dataclass(frozen=True, slots=True)
class MigrationStep:
    """Represents a migration step supporting universal or paired dialect SQL."""

    number: int
    up_sql: str | None = None
    down_sql: str | None = None
    sqlite_up_sql: str | None = None
    sqlite_down_sql: str | None = None
    postgres_up_sql: str | None = None
    postgres_down_sql: str | None = None

    @staticmethod
    def universal(number: int, up_sql: str, down_sql: str | None = None) -> "MigrationStep":
        return MigrationStep(number=number, up_sql=up_sql, down_sql=down_sql)

    @staticmethod
    def paired(
        number: int,
        sqlite_up_sql: str,
        postgres_up_sql: str,
        sqlite_down_sql: str | None = None,
        postgres_down_sql: str | None = None,
    ) -> "MigrationStep":
        return MigrationStep(
            number=number,
            sqlite_up_sql=sqlite_up_sql,
            postgres_up_sql=postgres_up_sql,
            sqlite_down_sql=sqlite_down_sql,
            postgres_down_sql=postgres_down_sql,
        )

    def get_up_sql(self, dialect: DbDialect) -> str:
        if dialect == "sqlite" and self.sqlite_up_sql is not None:
            return self.sqlite_up_sql
        if dialect == "postgres" and self.postgres_up_sql is not None:
            return self.postgres_up_sql
        if self.up_sql is not None:
            return self.up_sql
        raise ValueError(f"No up SQL defined for step {self.number} on dialect {dialect}.")

    def get_down_sql(self, dialect: DbDialect) -> str | None:
        if dialect == "sqlite" and self.sqlite_down_sql is not None:
            return self.sqlite_down_sql
        if dialect == "postgres" and self.postgres_down_sql is not None:
            return self.postgres_down_sql
        return self.down_sql

    def compute_fingerprint(self) -> str:
        """Compute SHA-256 fingerprint exactly matching C# and TypeScript."""
        up = self.up_sql if self.up_sql is not None else f"{self.sqlite_up_sql or ''};{self.postgres_up_sql or ''}"
        down = self.down_sql if self.down_sql is not None else (
            f"{self.sqlite_down_sql or ''};{self.postgres_down_sql or ''}"
            if (self.sqlite_down_sql is not None or self.postgres_down_sql is not None)
            else None
        )
        down_len = len(down) if down is not None else -1
        raw = f"{self.number}:{len(up)}:{up}:{down_len}:{down or ''}"
        return hashlib.sha256(raw.encode("utf-8")).hexdigest().lower()
