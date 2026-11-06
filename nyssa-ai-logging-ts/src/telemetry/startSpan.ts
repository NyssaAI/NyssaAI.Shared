// Responsibility: Start and manage OpenTelemetry spans with AsyncLocalStorage correlation for structured logs.
// Public interface:
// +-- getActiveSpan()
// `-- startSpan(name, attributes, operationOrCallback, callback)

import { context, trace } from "@opentelemetry/api";
import { AsyncLocalStorage } from "node:async_hooks";
import { SpanScope } from "./SpanScope.js";

const ACTIVE_SPAN_STORAGE = new AsyncLocalStorage<SpanScope | null>();
let fallbackSpanStack: SpanScope[] = [];

/** Return the currently active SpanScope in the current async execution context, if any. */
export function getActiveSpan(): SpanScope | null {
  const fromStorage = ACTIVE_SPAN_STORAGE.getStore();
  if (fromStorage !== undefined) {
    return fromStorage;
  }
  return fallbackSpanStack.length > 0
    ? (fallbackSpanStack[fallbackSpanStack.length - 1] ?? null)
    : null;
}

/**
 * Start a diagnostic span correlated with Nyssa structured logs and OpenTelemetry.
 *
 * Can be used either with a scoped callback (`startSpan("checkout", { OrderId: 1042 }, async (span) => { ... })`)
 * or manually (`const span = startSpan("checkout", { OrderId: 1042 }); try { ... } finally { span.end(); }`).
 */
export function startSpan<T = SpanScope>(
  name: string,
  attributes?: Readonly<Record<string, unknown>>,
  operationOrCallback?: string | ((span: SpanScope) => T),
  callback?: (span: SpanScope) => T
): T {
  const operation = typeof operationOrCallback === "string" ? operationOrCallback : name;
  const fn = typeof operationOrCallback === "function" ? operationOrCallback : callback;

  const parentSpan = getActiveSpan();
  const tracer = trace.getTracer("nyssa-ai-logging");
  const otelSpan = tracer.startSpan(name);
  const otelCtx = trace.setSpan(context.active(), otelSpan);

  if (fn !== undefined) {
    const scope = new SpanScope(name, attributes, operation, otelSpan, null, parentSpan);
    return ACTIVE_SPAN_STORAGE.run(scope, () =>
      context.with(otelCtx, () => {
        try {
          const result = fn(scope);
          if (result instanceof Promise) {
            return result.then(
              val => {
                scope.end();
                return val;
              },
              err => {
                scope.end(err);
                throw err;
              }
            ) as unknown as T;
          }
          scope.end();
          return result;
        } catch (err) {
          scope.end(err);
          throw err;
        }
      })
    );
  }

  let restored = false;
  const scope = new SpanScope(
    name,
    attributes,
    operation,
    otelSpan,
    () => {
      if (restored) {
        return;
      }
      restored = true;
      fallbackSpanStack = fallbackSpanStack.filter(s => s !== scope);
    },
    parentSpan
  );
  fallbackSpanStack.push(scope);
  return scope as unknown as T;
}
