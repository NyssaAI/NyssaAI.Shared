// Responsibility: Contract for operational boundary logging complying with TS-LOG.
// Public interface:
// `-- IConfigurationLogger
//     `-- log(record)

import type { LogRecord } from "./LogRecord.js";

/**
 * IConfigurationLogger receives structured operational log records from ConfigurationClient.
 */
export interface IConfigurationLogger {
  /**
   * Emits a structured log record. Must never throw or interrupt domain flow.
   */
  log(record: LogRecord): void;
}
