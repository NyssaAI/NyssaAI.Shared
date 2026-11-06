// Responsibility: Default operational logger forwarding configuration records to @nyssa-ai/logging with sink containment.
// Public interface:
// `-- NullConfigurationLogger
//     `-- log(record)

import { getLogger } from "@nyssa-ai/logging";
import type { IConfigurationLogger } from "./IConfigurationLogger.js";
import type { LogRecord } from "./LogRecord.js";

const configurationPinoLogger = getLogger("nyssa_ai_configuration");

/**
 * Default configuration logger forwarding operational records to `@nyssa-ai/logging` (`getLogger("nyssa_ai_configuration")`).
 */
export class NullConfigurationLogger {
  public log(record: LogRecord): void {
    try {
      const fields: Record<string, unknown> = { ...record };
      if (record.level === "error") {
        configurationPinoLogger.error(
          "Configuration {operation} failed ({event})",
          record.error,
          fields,
          record.operation
        );
      } else if (record.level === "warn") {
        configurationPinoLogger.warn(
          "Configuration {operation} degraded ({event})",
          fields,
          record.operation
        );
      } else if (record.level === "debug") {
        configurationPinoLogger.debug(
          "Configuration {operation} ({event})",
          fields,
          record.operation
        );
      } else {
        configurationPinoLogger.info(
          "Configuration {operation} completed ({event})",
          fields,
          record.operation
        );
      }
    } catch {
      // Sink containment
    }
  }
}

const _typeCheck: IConfigurationLogger = null as unknown as NullConfigurationLogger;
void _typeCheck;

