// Responsibility: Define log level values and level severity comparison logic.
// Public interface:
// +-- LogLevel
// +-- isLogLevelEnabled(level, minimumLevel)
// `-- parseLogLevel(value)

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
      return "Critical";
    case "none":
      return "None";
    default:
      return "Information";
  }
}

export function isLogLevelEnabled(level: LogLevel, minimumLevel: LogLevel): boolean {
  if (level === "None") {
    return false;
  }
  const levelSeverity = LEVEL_SEVERITY[level] ?? 2;
  const minimumSeverity = LEVEL_SEVERITY[minimumLevel] ?? 2;
  return levelSeverity >= minimumSeverity;
}
