// Responsibility: Factory function configuring default Nyssa logging with standard error and optional JSON file.
// Public interface:
// `-- createNyssaLogging(filePath, minimumLevel, clock)

import type { Clock } from "../fileLogging/Clock.js";
import type { LogLevel } from "../LogLevel.js";
import { LoggingBuilder } from "./LoggingBuilder.js";

export function createNyssaLogging(
  filePath?: string,
  minimumLevel: LogLevel = "Information",
  clock?: Clock
): LoggingBuilder {
  const builder = new LoggingBuilder(minimumLevel, clock);
  builder.withStdErr(minimumLevel);
  if (filePath) {
    builder.addNyssaJsonFile(filePath, minimumLevel);
  }
  return builder;
}
