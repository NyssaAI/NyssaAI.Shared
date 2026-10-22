// Responsibility: No-op fallback logger that safely contains all dispatches.
// Public interface:
// +-- NullConfigurationLogger
// |   `-- log(record)

import type { IConfigurationLogger, LogRecord } from "./IConfigurationLogger.js";

/**
 * NullConfigurationLogger discards all records silently.
 */
export class NullConfigurationLogger implements IConfigurationLogger {
  public log(_record: LogRecord): void {
    // no-op
  }
}
