// Responsibility: Provide process-wide category loggers backed by the active Pino LoggingBuilder without injection.
// Public interface:
// +-- getActiveLoggingBuilder()
// +-- getLogger(categoryName, bindings)
// `-- setActiveLoggingBuilder(builder)

import type { Logger } from "./Logger.js";
import { LoggingBuilder } from "./configuration/LoggingBuilder.js";

let activeBuilder: LoggingBuilder | null = null;

function ensureDefaultBuilder(): LoggingBuilder {
  if (activeBuilder === null) {
    activeBuilder = new LoggingBuilder("Warning");
  }
  return activeBuilder;
}

/** Return the currently active process-wide LoggingBuilder. */
export function getActiveLoggingBuilder(): LoggingBuilder {
  return ensureDefaultBuilder();
}

/** Replace the process-wide LoggingBuilder, disposing any previous builder. */
export function setActiveLoggingBuilder(builder: LoggingBuilder): LoggingBuilder {
  if (activeBuilder !== null && activeBuilder !== builder) {
    activeBuilder.dispose();
  }
  activeBuilder = builder;
  return builder;
}

/**
 * Retrieve a category-scoped `Logger` backed by the active Pino `LoggingBuilder`.
 *
 * Safe to call at module import time before `configureNyssaLogging()` runs:
 * every log operation delegates dynamically to the active builder.
 */
export function getLogger(
  categoryName: string,
  bindings?: Readonly<Record<string, unknown>>
): Logger {
  let cachedBuilder: LoggingBuilder | null = null;
  let cachedLogger: Logger | null = null;

  const resolve = (): Logger => {
    const current = ensureDefaultBuilder();
    if (cachedBuilder !== current || cachedLogger === null) {
      cachedBuilder = current;
      cachedLogger = current.createLogger(categoryName, bindings);
    }
    return cachedLogger;
  };

  return {
    trace: (m, f, op) => resolve().trace(m, f, op),
    debug: (m, f, op) => resolve().debug(m, f, op),
    info: (m, f, op) => resolve().info(m, f, op),
    warn: (m, f, op) => resolve().warn(m, f, op),
    error: (m, e, f, op) => resolve().error(m, e, f, op),
    critical: (m, e, f, op) => resolve().critical(m, e, f, op),
    fatal: (m, e, f, op) => resolve().fatal(m, e, f, op),
    child: childBindings =>
      getLogger(categoryName, { ...(bindings ?? {}), ...childBindings }),
    isEnabled: level => resolve().isEnabled(level)
  };
}
