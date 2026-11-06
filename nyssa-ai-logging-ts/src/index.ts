// Responsibility: Export the public surface of @nyssa-ai/logging.
// Public interface:
// +-- LogFile
// +-- LogRecordSerializer
// +-- LoggingBuilder
// +-- OtelLogBridge
// +-- OwnerOnlyAccess
// +-- SpanScope
// +-- StderrDiagnostics
// +-- SystemClock
// +-- configureNyssaLogging
// +-- createNyssaLogging
// +-- extractNyssaRecord
// +-- fromPinoLevel
// +-- getActiveLoggingBuilder
// +-- getActiveSpan
// +-- getLogger
// +-- index
// +-- isLogLevelEnabled
// +-- parseLogLevel
// +-- setActiveLoggingBuilder
// +-- startSpan
// +-- toPinoLevelName
// `-- toPinoLevelNumber

import { configureNyssaLogging as configureNyssaLoggingFn } from "./configuration/configureNyssaLogging.js";
import { createNyssaLogging as createNyssaLoggingFn } from "./configuration/createNyssaLogging.js";
import { LoggingBuilder as LoggingBuilderClass } from "./configuration/LoggingBuilder.js";
import { extractNyssaRecord as extractNyssaRecordFn } from "./fileLogging/extractNyssaRecord.js";
import { LogFile as LogFileClass } from "./fileLogging/LogFile.js";
import { LogRecordSerializer as LogRecordSerializerClass } from "./fileLogging/LogRecordSerializer.js";
import { OwnerOnlyAccess as OwnerOnlyAccessClass } from "./fileLogging/OwnerOnlyAccess.js";
import { StderrDiagnostics as StderrDiagnosticsClass } from "./fileLogging/StderrDiagnostics.js";
import { SystemClock as SystemClockClass } from "./fileLogging/SystemClock.js";
import {
  getActiveLoggingBuilder as getActiveLoggingBuilderFn,
  getLogger as getLoggerFn,
  setActiveLoggingBuilder as setActiveLoggingBuilderFn
} from "./getLogger.js";
import {
  fromPinoLevel as fromPinoLevelFn,
  isLogLevelEnabled as isLogLevelEnabledFn,
  parseLogLevel as parseLogLevelFn,
  toPinoLevelName as toPinoLevelNameFn,
  toPinoLevelNumber as toPinoLevelNumberFn
} from "./LogLevel.js";
import { OtelLogBridge as OtelLogBridgeClass } from "./telemetry/OtelLogBridge.js";
import { SpanScope as SpanScopeClass } from "./telemetry/SpanScope.js";
import {
  getActiveSpan as getActiveSpanFn,
  startSpan as startSpanFn
} from "./telemetry/startSpan.js";

/** Configure process-wide Nyssa Pino logging in one call with safe defaults. */
export const configureNyssaLogging = configureNyssaLoggingFn;

/** Legacy factory configuring Nyssa logging with standard error and optional JSON file. */
export const createNyssaLogging = createNyssaLoggingFn;

/** Retrieve a category-scoped Logger backed by the active Pino LoggingBuilder. */
export const getLogger = getLoggerFn;

/** Return the currently active process-wide LoggingBuilder. */
export const getActiveLoggingBuilder = getActiveLoggingBuilderFn;

/** Replace the process-wide LoggingBuilder, disposing any previous builder. */
export const setActiveLoggingBuilder = setActiveLoggingBuilderFn;

/** Fluent builder managing Pino logger hierarchy, rotating JSONL files, and OpenTelemetry. */
export const LoggingBuilder = LoggingBuilderClass;

/** Start a correlated OpenTelemetry span. */
export const startSpan = startSpanFn;

/** Return the currently active SpanScope in the current async execution context, if any. */
export const getActiveSpan = getActiveSpanFn;

/** Active OpenTelemetry span scope class. */
export const SpanScope = SpanScopeClass;

/** OpenTelemetry Logs API bridge class. */
export const OtelLogBridge = OtelLogBridgeClass;

/** Parse a case-insensitive string into a canonical Nyssa LogLevel. */
export const parseLogLevel = parseLogLevelFn;

/** Determine whether a log record at `level` meets or exceeds `minimumLevel`. */
export const isLogLevelEnabled = isLogLevelEnabledFn;

/** Convert a Nyssa LogLevel into its corresponding Pino level string. */
export const toPinoLevelName = toPinoLevelNameFn;

/** Convert a Nyssa LogLevel into its corresponding Pino numeric level. */
export const toPinoLevelNumber = toPinoLevelNumberFn;

/** Convert a Pino numeric or string level into a canonical Nyssa LogLevel. */
export const fromPinoLevel = fromPinoLevelFn;

/** Convert a parsed Pino JSON payload into a canonical Nyssa LogRecord. */
export const extractNyssaRecord = extractNyssaRecordFn;

/** Rotating JSONL file manager class. */
export const LogFile = LogFileClass;

/** Canonical Nyssa JSON log record serializer class. */
export const LogRecordSerializer = LogRecordSerializerClass;

/** Cross-platform owner-only file and directory access helper class. */
export const OwnerOnlyAccess = OwnerOnlyAccessClass;

/** Self-diagnostic stderr writer for log file failures. */
export const StderrDiagnostics = StderrDiagnosticsClass;

/** Default UTC system clock class. */
export const SystemClock = SystemClockClass;

/** Package barrel index reference. */
export const index = configureNyssaLoggingFn;
