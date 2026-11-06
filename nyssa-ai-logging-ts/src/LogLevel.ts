// Responsibility: Define log level values, Pino level mappings, and level severity comparison logic.
// Public interface:
// +-- LogLevel
// +-- fromPinoLevel(pinoLevel)
// +-- isLogLevelEnabled(level, minimumLevel)
// +-- parseLogLevel(value)
// +-- toPinoLevelName(level)
// `-- toPinoLevelNumber(level)

export type LogLevel =
  | "Trace"
  | "Debug"
  | "Information"
  | "Warning"
  | "Error"
  | "Critical"
  | "None";

const LEVEL_SEVERITY: Readonly<Record<LogLevel, number>> = {
  Trace: 0,
  Debug: 1,
  Information: 2,
  Warning: 3,
  Error: 4,
  Critical: 5,
  None: 6
};

const PINO_LEVEL_NAMES: Readonly<Record<LogLevel, string>> = {
  Trace: "trace",
  Debug: "debug",
  Information: "info",
  Warning: "warn",
  Error: "error",
  Critical: "fatal",
  None: "silent"
};

const PINO_LEVEL_NUMBERS: Readonly<Record<LogLevel, number>> = {
  Trace: 10,
  Debug: 20,
  Information: 30,
  Warning: 40,
  Error: 50,
  Critical: 60,
  None: 100
};

/** Parse a case-insensitive string into a canonical Nyssa LogLevel. */
export function parseLogLevel(value: string | undefined): LogLevel {
  if (!value) {
    return "Information";
  }
  const normalized = value.trim().toLowerCase();
  switch (normalized) {
    case "trace":
      return "Trace";
    case "debug":
      return "Debug";
    case "information":
    case "info":
      return "Information";
    case "warning":
    case "warn":
      return "Warning";
    case "error":
      return "Error";
    case "critical":
    case "crit":
    case "fatal":
      return "Critical";
    case "none":
    case "silent":
      return "None";
    default:
      return "Information";
  }
}

/** Determine whether a log record at `level` meets or exceeds `minimumLevel`. */
export function isLogLevelEnabled(level: LogLevel, minimumLevel: LogLevel): boolean {
  if (level === "None" || minimumLevel === "None") {
    return false;
  }
  const levelSeverity = LEVEL_SEVERITY[level] ?? 2;
  const minimumSeverity = LEVEL_SEVERITY[minimumLevel] ?? 2;
  return levelSeverity >= minimumSeverity;
}

/** Convert a Nyssa LogLevel into its corresponding Pino level string (`trace`..`fatal`/`silent`). */
export function toPinoLevelName(level: LogLevel): string {
  return PINO_LEVEL_NAMES[level] ?? "info";
}

/** Convert a Nyssa LogLevel into its corresponding Pino numeric level (`10`..`60`/`100`). */
export function toPinoLevelNumber(level: LogLevel): number {
  return PINO_LEVEL_NUMBERS[level] ?? 30;
}

/** Convert a Pino numeric or string level into a canonical Nyssa LogLevel. */
export function fromPinoLevel(pinoLevel: number | string): LogLevel {
  if (typeof pinoLevel === "string") {
    return parseLogLevel(pinoLevel);
  }
  if (pinoLevel >= 100) {
    return "None";
  }
  if (pinoLevel >= 60) {
    return "Critical";
  }
  if (pinoLevel >= 50) {
    return "Error";
  }
  if (pinoLevel >= 40) {
    return "Warning";
  }
  if (pinoLevel >= 30) {
    return "Information";
  }
  if (pinoLevel >= 20) {
    return "Debug";
  }
  return "Trace";
}

