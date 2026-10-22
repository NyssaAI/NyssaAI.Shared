// Responsibility: No-op fallback logger that safely contains all dispatches.
// Public interface:
// `-- NullConfigurationLogger
//     `-- log(_record)

import type { IConfigurationLogger } from "./IConfigurationLogger.js";
import type { LogRecord } from "./LogRecord.js";

/**
 * NullConfigurationLogger discards all records silently.
 */
export class NullConfigurationLogger {
  public log(_record: LogRecord): void {
    // no-op
  }
}

const _typeCheck: IConfigurationLogger = null as unknown as NullConfigurationLogger;
void _typeCheck;
