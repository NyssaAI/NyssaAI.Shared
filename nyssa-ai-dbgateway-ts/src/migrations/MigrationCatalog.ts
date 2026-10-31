// Responsibility: Validate and snapshot migration steps, release pins, and SHA-256 fingerprints.
// Public interface:
// `-- MigrationCatalog
//     +-- commandTimeoutSeconds
//     +-- create(steps, releases, commandTimeoutSeconds)
//     +-- fingerprint(step, dialect)
//     +-- releases
//     +-- resolve(release)
//     `-- steps

import crypto from "node:crypto";
import type { DbDialect } from "../DbDialect.js";
import type { MigrationStep } from "./MigrationStep.js";
import type { MigrationReleasePin } from "./MigrationReleasePin.js";
import { MigrationSql } from "./MigrationSql.js";

const SEMVER_PATTERN =
  /^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-((?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*)(?:\.(?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*))*))?(?:\+([0-9a-zA-Z-]+(?:\.[0-9a-zA-Z-]+)*))?$/;

export class MigrationCatalog {
  public readonly steps: readonly MigrationStep[];
  public readonly releases: ReadonlyMap<string, number>;
  public readonly commandTimeoutSeconds: number;

  private constructor(
    steps: readonly MigrationStep[],
    releases: ReadonlyMap<string, number>,
    commandTimeoutSeconds: number
  ) {
    this.steps = steps;
    this.releases = releases;
    this.commandTimeoutSeconds = commandTimeoutSeconds;
  }

  public static create(
    steps: readonly MigrationStep[],
    releases: readonly MigrationReleasePin[],
    commandTimeoutSeconds = 30
  ): MigrationCatalog {
    if (!steps) {
      throw new Error("Migration steps are required.");
    }
    if (!releases) {
      throw new Error("Migration release pins are required.");
    }
    if (releases.length === 0) {
      throw new Error("Migration registration requires release pins.");
    }
    if (commandTimeoutSeconds < 0) {
      throw new RangeError("Command timeout cannot be negative.");
    }

    const ordered = [...steps].sort((a, b) => a.number - b.number);
    for (let index = 0; index < ordered.length; index++) {
      const step = ordered[index]!;
      if (step.number !== index + 1) {
        throw new Error("Migration steps must be contiguous starting at 1.");
      }
      if (step.commandTimeoutSeconds !== undefined && step.commandTimeoutSeconds < 0) {
        throw new RangeError("Migration step timeouts cannot be negative.");
      }
      if (
        !MigrationSql.isTransactional(step.sqliteUpSql) ||
        !MigrationSql.isTransactional(step.postgresUpSql) ||
        (step.sqliteDownSql && !MigrationSql.isTransactional(step.sqliteDownSql)) ||
        (step.postgresDownSql && !MigrationSql.isTransactional(step.postgresDownSql))
      ) {
        throw new Error("Migration SQL must contain valid transactional statements.");
      }
    }

    const releaseMap = new Map<string, number>();
    for (const pin of releases) {
      if (!pin.release || !SEMVER_PATTERN.test(pin.release)) {
        throw new Error("Migration release pins must be exact full SemVer strings.");
      }
      if (pin.number < 0 || pin.number > ordered.length) {
        throw new RangeError("Migration release pins must target an existing step or 0.");
      }
      if (releaseMap.has(pin.release)) {
        throw new Error(`Duplicate migration release pin: ${pin.release}`);
      }
      releaseMap.set(pin.release, pin.number);
    }

    return new MigrationCatalog(ordered, releaseMap, commandTimeoutSeconds);
  }

  public resolve(release: string): number {
    if (!release || typeof release !== "string") {
      throw new Error("Release identifier must be a non-empty string.");
    }
    const target = this.releases.get(release);
    if (target === undefined) {
      throw new Error(`The requested migration release "${release}" is not registered.`);
    }
    return target;
  }

  public static fingerprint(step: MigrationStep, dialect: DbDialect): string {
    const upSql = step.getUpSql(dialect);
    const downSql = step.getDownSql(dialect);
    const downLength = downSql !== undefined && downSql !== null ? downSql.length : -1;
    const downPart = downSql ?? "";
    const text = `${step.number}:${upSql.length}:${upSql}:${downLength}:${downPart}`;
    return crypto.createHash("sha256").update(Buffer.from(text, "utf8")).digest("hex").toLowerCase();
  }
}
