// Responsibility: Structured operational logging contract backed by Pino.
// Public interface:
// `-- Logger
//     +-- child(bindings)
//     +-- critical(messageOrFields, errorOrMessage, fields, operation)
//     +-- debug(messageOrFields, fieldsOrMessage, operation)
//     +-- error(messageOrFields, errorOrMessage, fields, operation)
//     +-- fatal(messageOrFields, errorOrMessage, fields, operation)
//     +-- info(messageOrFields, fieldsOrMessage, operation)
//     +-- isEnabled(level)
//     +-- trace(messageOrFields, fieldsOrMessage, operation)
//     `-- warn(messageOrFields, fieldsOrMessage, operation)

import type { LogLevel } from "./LogLevel.js";

/**
 * Structured operational logger supporting both Nyssa (`logger.info(msg, fields, op)`)
 * and Pino (`logger.info(fields, msg)`) calling conventions.
 */
export interface Logger {
  trace(
    messageOrFields: string | Readonly<Record<string, unknown>>,
    fieldsOrMessage?: Readonly<Record<string, unknown>> | string,
    operation?: string
  ): void;
  debug(
    messageOrFields: string | Readonly<Record<string, unknown>>,
    fieldsOrMessage?: Readonly<Record<string, unknown>> | string,
    operation?: string
  ): void;
  info(
    messageOrFields: string | Readonly<Record<string, unknown>>,
    fieldsOrMessage?: Readonly<Record<string, unknown>> | string,
    operation?: string
  ): void;
  warn(
    messageOrFields: string | Readonly<Record<string, unknown>>,
    fieldsOrMessage?: Readonly<Record<string, unknown>> | string,
    operation?: string
  ): void;
  error(
    messageOrFields: string | Readonly<Record<string, unknown>>,
    errorOrMessage?: unknown,
    fields?: Readonly<Record<string, unknown>>,
    operation?: string
  ): void;
  critical(
    messageOrFields: string | Readonly<Record<string, unknown>>,
    errorOrMessage?: unknown,
    fields?: Readonly<Record<string, unknown>>,
    operation?: string
  ): void;
  fatal(
    messageOrFields: string | Readonly<Record<string, unknown>>,
    errorOrMessage?: unknown,
    fields?: Readonly<Record<string, unknown>>,
    operation?: string
  ): void;
  child(bindings: Readonly<Record<string, unknown>>): Logger;
  isEnabled(level: LogLevel | string): boolean;
}

