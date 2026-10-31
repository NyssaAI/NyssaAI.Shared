// Responsibility: Load migration steps and release pins from a filesystem directory.
// Public interface:
// `-- loadMigrationCatalog(directoryPath, defaultRelease, commandTimeoutSeconds)

import fs from "node:fs";
import path from "node:path";
import { MigrationStep } from "./MigrationStep.js";
import { MigrationReleasePin } from "./MigrationReleasePin.js";
import { MigrationCatalog } from "./MigrationCatalog.js";

export function loadMigrationCatalog(
  directoryPath: string,
  defaultRelease?: string,
  commandTimeoutSeconds = 30
): MigrationCatalog {
  if (!fs.existsSync(directoryPath)) {
    throw new Error(`Migration directory not found: ${directoryPath}`);
  }

  const entries = fs.readdirSync(directoryPath);
  const stepMap = new Map<
    number,
    {
      sqliteUp?: string;
      postgresUp?: string;
      sqliteDown?: string;
      postgresDown?: string;
    }
  >();

  for (const file of entries) {
    const filePath = path.join(directoryPath, file);
    if (!fs.statSync(filePath).isFile()) {
      continue;
    }

    // Match {number}_{name}...
    const match = /^(\d+)[-_](.+)$/.exec(file);
    if (!match) {
      continue;
    }
    const stepNumber = parseInt(match[1]!, 10);
    const remainder = match[2]!.toLowerCase();
    const content = fs.readFileSync(filePath, "utf8").trim();

    let slot = stepMap.get(stepNumber);
    if (!slot) {
      slot = {};
      stepMap.set(stepNumber, slot);
    }

    if (remainder.endsWith(".sqlite.sql")) {
      slot.sqliteUp = content;
    } else if (remainder.endsWith(".postgres.sql")) {
      slot.postgresUp = content;
    } else if (remainder.endsWith(".sqlite.down.sql")) {
      slot.sqliteDown = content;
    } else if (remainder.endsWith(".postgres.down.sql")) {
      slot.postgresDown = content;
    } else if (remainder.endsWith(".down.sql")) {
      slot.sqliteDown = content;
      slot.postgresDown = content;
    } else if (remainder.endsWith(".sql")) {
      slot.sqliteUp = content;
      slot.postgresUp = content;
    }
  }

  const steps: MigrationStep[] = [];
  const sortedNumbers = Array.from(stepMap.keys()).sort((a, b) => a - b);

  for (const num of sortedNumbers) {
    const slot = stepMap.get(num)!;
    if (!slot.sqliteUp || !slot.postgresUp) {
      throw new Error(`Incomplete migration step ${num}: missing up SQL.`);
    }
    steps.push(
      new MigrationStep({
        number: num,
        sqliteUpSql: slot.sqliteUp,
        postgresUpSql: slot.postgresUp,
        sqliteDownSql: slot.sqliteDown,
        postgresDownSql: slot.postgresDown
      })
    );
  }

  const pins: MigrationReleasePin[] = [];
  const lockFilePath = path.join(directoryPath, "migrations.lock");
  if (fs.existsSync(lockFilePath)) {
    const lockLines = fs.readFileSync(lockFilePath, "utf8").split("\n");
    for (const line of lockLines) {
      const trimmed = line.trim();
      if (!trimmed || trimmed.startsWith("#")) {
        continue;
      }
      const parts = trimmed.split("=");
      if (parts.length === 2) {
        pins.push(new MigrationReleasePin(parts[0]!.trim(), parseInt(parts[1]!.trim(), 10)));
      }
    }
  } else if (defaultRelease) {
    pins.push(new MigrationReleasePin(defaultRelease, steps.length));
  } else if (steps.length > 0) {
    pins.push(new MigrationReleasePin("1.0.0", steps.length));
  }

  return MigrationCatalog.create(steps, pins, commandTimeoutSeconds);
}
