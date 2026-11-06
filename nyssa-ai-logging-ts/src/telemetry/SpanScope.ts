// Responsibility: Active OpenTelemetry span scope with deterministic correlation IDs and attribute tracking.
// Public interface:
// `-- SpanScope
//     +-- attributes
//     +-- end(error)
//     +-- name
//     +-- new(name, attributes, operation, otelSpan, restoreContext, parentSpan)
//     +-- operation
//     +-- parentSpanId
//     +-- recordException(error)
//     +-- setAttribute(key, value)
//     +-- setStatusError(description)
//     +-- spanId
//     `-- traceId

import { SpanStatusCode, type Span as OtelSpan } from "@opentelemetry/api";
import * as crypto from "node:crypto";

const INVALID_TRACE_ID = "00000000000000000000000000000000";
const INVALID_SPAN_ID = "0000000000000000";

function coerceOtelAttribute(value: unknown): string | number | boolean {
  if (typeof value === "string" || typeof value === "number" || typeof value === "boolean") {
    return value;
  }
  return String(value);
}

/**
 * Represents an active diagnostic span for correlating structured logs and OpenTelemetry traces.
 */
export class SpanScope {
  public readonly name: string;
  private readonly attrs: Record<string, unknown>;
  private readonly op: string;
  private readonly otelSpan: OtelSpan | null;
  private readonly restoreContext: (() => void) | null;
  private readonly localTraceId: string;
  private readonly localSpanId: string;
  private readonly localParentSpanId: string | null;
  private ended = false;

  public constructor(
    name: string,
    attributes?: Readonly<Record<string, unknown>>,
    operation?: string,
    otelSpan?: OtelSpan | null,
    restoreContext?: (() => void) | null,
    parentSpan?: SpanScope | null
  ) {
    this.name = name;
    this.attrs = attributes ? { ...attributes } : {};
    this.op = operation ?? name;
    this.otelSpan = otelSpan ?? null;
    this.restoreContext = restoreContext ?? null;

    let resolvedTraceId = "";
    let resolvedSpanId = "";
    if (this.otelSpan !== null) {
      const ctx = this.otelSpan.spanContext();
      if (ctx.traceId && ctx.traceId !== INVALID_TRACE_ID) {
        resolvedTraceId = ctx.traceId;
      }
      if (ctx.spanId && ctx.spanId !== INVALID_SPAN_ID) {
        resolvedSpanId = ctx.spanId;
      }
    }

    this.localTraceId =
      resolvedTraceId ||
      (parentSpan ? parentSpan.traceId : crypto.randomBytes(16).toString("hex"));
    this.localSpanId = resolvedSpanId || crypto.randomBytes(8).toString("hex");
    this.localParentSpanId = parentSpan ? parentSpan.spanId : null;

    if (this.otelSpan !== null && attributes) {
      for (const [k, v] of Object.entries(attributes)) {
        this.otelSpan.setAttribute(k, coerceOtelAttribute(v));
      }
    }
  }

  /** Return the 32-character hexadecimal trace identifier. */
  public get traceId(): string {
    return this.localTraceId;
  }

  /** Return the 16-character hexadecimal span identifier. */
  public get spanId(): string {
    return this.localSpanId;
  }

  /** Return the parent span's 16-character hexadecimal identifier, if nested. */
  public get parentSpanId(): string | null {
    return this.localParentSpanId;
  }

  /** Return the operation name associated with this span. */
  public get operation(): string {
    return this.op;
  }

  /** Return a shallow copy of the attributes attached to this span. */
  public get attributes(): Readonly<Record<string, unknown>> {
    return { ...this.attrs };
  }

  /** Attach or update a key-value attribute on the active span. */
  public setAttribute(key: string, value: unknown): this {
    this.attrs[key] = value;
    if (this.otelSpan !== null) {
      try {
        this.otelSpan.setAttribute(key, coerceOtelAttribute(value));
      } catch {
        // Sink containment
      }
    }
    return this;
  }

  /** Record an exception on the underlying OpenTelemetry span. */
  public recordException(error: unknown): this {
    if (this.otelSpan !== null) {
      try {
        if (error instanceof Error) {
          this.otelSpan.recordException(error);
        } else {
          this.otelSpan.recordException(String(error));
        }
      } catch {
        // Sink containment
      }
    }
    return this;
  }

  /** Mark the span status as ERROR with a human-readable description. */
  public setStatusError(description: string): this {
    if (this.otelSpan !== null) {
      try {
        this.otelSpan.setStatus({ code: SpanStatusCode.ERROR, message: description });
      } catch {
        // Sink containment
      }
    }
    return this;
  }

  /** End the span and restore the previous active span context. */
  public end(error?: unknown): void {
    if (this.ended) {
      return;
    }
    this.ended = true;
    if (error !== undefined && error !== null) {
      const msg = error instanceof Error ? error.message : String(error);
      this.recordException(error);
      this.setStatusError(msg);
    }
    if (this.otelSpan !== null) {
      try {
        this.otelSpan.end();
      } catch {
        // Sink containment
      }
    }
    if (this.restoreContext !== null) {
      this.restoreContext();
    }
  }
}
