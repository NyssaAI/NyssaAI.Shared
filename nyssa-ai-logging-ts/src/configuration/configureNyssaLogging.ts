// Responsibility: Configure process-wide Nyssa Pino logging with safe defaults, rotating JSON files, and OpenTelemetry.
// Public interface:
// `-- configureNyssaLogging(options)

import * as path from "node:path";
import type { NyssaLoggingOptions } from "./NyssaLoggingOptions.js";
import { LoggingBuilder } from "./LoggingBuilder.js";
import { setActiveLoggingBuilder } from "../getLogger.js";
import { parseLogLevel } from "../LogLevel.js";

function sanitizeAppName(rawName: string | undefined): string {
  const trimmed = (rawName ?? "").trim();
  const sanitized = trimmed
    .replace(/[^A-Za-z0-9._-]+/g, "-")
    .replace(/^[-._]+|[-._]+$/g, "");
  return sanitized.length > 0 ? sanitized : "application";
}

function defaultAppName(): string {
  const envName = process.env["NYSSA_APP_NAME"];
  if (envName && envName.trim().length > 0) {
    return sanitizeAppName(envName);
  }
  const argv1 = process.argv[1];
  if (argv1 && argv1.trim().length > 0) {
    const ext = path.extname(argv1);
    const stem = path.basename(argv1, ext);
    if (stem && stem !== "-e" && stem !== "vitest") {
      return sanitizeAppName(stem);
    }
  }
  return "application";
}

/**
 * Configure process-wide Nyssa Pino logging in one call with safe defaults:
 * - Standard error human-readable output (`stderr: true` by default)
 * - Rotating JSONL file in `./logs/{yyyy.mm.dd}-{appName}-log.json` (`10 MiB` rotation, `5` backups, `30`-day pruning, `0600` permissions)
 * - Optional per-package level overrides (`packageLevels: { "a.b.c": "Debug" }`)
 * - Optional OpenTelemetry log bridging (`enableOtel: true`)
 */
export function configureNyssaLogging(options?: NyssaLoggingOptions): LoggingBuilder {
  const opts: NyssaLoggingOptions = options ?? {};
  const rawLevel =
    opts.minimumLevel ?? process.env["NYSSA_LOG_LEVEL"] ?? "Information";
  const resolvedLevel = parseLogLevel(String(rawLevel));

  const builder = new LoggingBuilder(resolvedLevel, opts.clock);

  if (opts.packageLevels) {
    for (const [pkg, lvl] of Object.entries(opts.packageLevels)) {
      builder.setPackageLevel(pkg, lvl);
    }
  }

  const useStderr = opts.stderr ?? true;
  if (useStderr) {
    builder.withStdErr();
  }

  if (opts.console === true) {
    builder.withConsole();
  }

  if (opts.filePath !== null && opts.filePath !== false) {
    if (typeof opts.filePath === "string" && opts.filePath.trim().length > 0) {
      builder.addNyssaJsonFile(opts.filePath);
    } else {
      const appName = sanitizeAppName(opts.appName ?? defaultAppName());
      const logDir =
        opts.logDir && opts.logDir.trim().length > 0
          ? path.resolve(opts.logDir)
          : path.resolve(process.cwd(), "logs");
      builder.withRotatingJsonLog(path.join(logDir, `${appName}-log.json`));
    }
  }

  if (opts.enableOtel === true) {
    builder.withOtel();
  }

  return setActiveLoggingBuilder(builder);
}
