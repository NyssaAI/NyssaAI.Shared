// Responsibility: Normalize Nyssa and Pino logger arguments and enrich records with active OpenTelemetry spans.
// Public interface:
// +-- enrichWithActiveSpan(fields, operation, error, timestamp)
// +-- normalizeErrorArgs(messageOrFields, errorOrMessage, fields)
// `-- normalizeNormalArgs(messageOrFields, fieldsOrMessage)

import { trace, type SpanContext } from "@opentelemetry/api";
import { getActiveSpan } from "../telemetry/startSpan.js";

const INVALID_TRACE_ID = "00000000000000000000000000000000";
const INVALID_SPAN_ID = "0000000000000000";

function formatErrorText(error: unknown): string | null {
  if (error === undefined || error === null) {
    return null;
  }
  if (error instanceof Error) {
    return error.stack ?? error.message;
  }
  return String(error);
}

/** Normalize Nyssa `(message, fields)` and Pino `(fields, message)` arguments for non-error levels. */
export function normalizeNormalArgs(
  messageOrFields: string | Readonly<Record<string, unknown>>,
  fieldsOrMessage?: Readonly<Record<string, unknown>> | string
): {
  readonly message: string;
  readonly fields: Readonly<Record<string, unknown>> | undefined;
} {
  if (typeof messageOrFields === "string") {
    return {
      message: messageOrFields,
      fields: typeof fieldsOrMessage === "object" ? fieldsOrMessage : undefined
    };
  }
  return {
    message: typeof fieldsOrMessage === "string" ? fieldsOrMessage : "",
    fields: messageOrFields
  };
}

/** Normalize Nyssa `(message, error, fields)` and Pino `(fields, message)` arguments for error/critical levels. */
export function normalizeErrorArgs(
  messageOrFields: string | Readonly<Record<string, unknown>>,
  errorOrMessage?: unknown,
  fields?: Readonly<Record<string, unknown>>
): {
  readonly message: string;
  readonly error: unknown;
  readonly resolvedFields: Readonly<Record<string, unknown>> | undefined;
} {
  if (typeof messageOrFields === "string") {
    return {
      message: messageOrFields,
      error: errorOrMessage,
      resolvedFields: fields
    };
  }
  const errProp = messageOrFields["err"] ?? messageOrFields["error"];
  return {
    message: typeof errorOrMessage === "string" ? errorOrMessage : "",
    error: errProp,
    resolvedFields: messageOrFields
  };
}

/** Merge active span attributes, trace/span IDs, operation, and exception text into a Pino payload object. */
export function enrichWithActiveSpan(
  fields: Readonly<Record<string, unknown>> | undefined,
  operation: string | undefined,
  error: unknown,
  timestamp: string
): Record<string, unknown> {
  const merged: Record<string, unknown> = {
    nyssaTimestamp: timestamp
  };

  const activeSpan = getActiveSpan();
  if (activeSpan !== null) {
    for (const [k, v] of Object.entries(activeSpan.attributes)) {
      merged[k] = v;
    }
    merged["TraceId"] = activeSpan.traceId;
    merged["SpanId"] = activeSpan.spanId;
    if (activeSpan.parentSpanId !== null) {
      merged["ParentSpanId"] = activeSpan.parentSpanId;
    }
  } else {
    const otelSpan = trace.getActiveSpan();
    const ctx: SpanContext | undefined = otelSpan?.spanContext();
    if (
      ctx &&
      ctx.traceId &&
      ctx.traceId !== INVALID_TRACE_ID &&
      ctx.spanId &&
      ctx.spanId !== INVALID_SPAN_ID
    ) {
      merged["TraceId"] = ctx.traceId;
      merged["SpanId"] = ctx.spanId;
    }
  }

  if (fields) {
    for (const [k, v] of Object.entries(fields)) {
      merged[k] = v;
    }
  }

  const resolvedOp =
    operation ??
    (typeof fields?.["operation"] === "string" ? fields["operation"] : undefined) ??
    (activeSpan !== null ? activeSpan.operation : "");
  if (resolvedOp) {
    merged["nyssaOperation"] = resolvedOp;
  }

  const formattedError = formatErrorText(error);
  if (formattedError !== null) {
    merged["nyssaException"] = formattedError;
  }

  return merged;
}
