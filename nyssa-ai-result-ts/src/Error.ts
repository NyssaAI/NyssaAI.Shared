// Responsibility: Represent sanitized or captured failure information as an immutable value object.
// Public interface:
// +-- CANCELED_USER_MESSAGE
// +-- DEFAULT_USER_MESSAGE
// +-- Error
// |   +-- CANCELED_USER_MESSAGE
// |   +-- CanceledUserMessage
// |   +-- DEFAULT_USER_MESSAGE
// |   +-- DefaultUserMessage
// |   +-- TIMED_OUT_USER_MESSAGE
// |   +-- TimedOutUserMessage
// |   +-- atLeastOne(errors)
// |   +-- canceled(canceled)
// |   +-- category
// |   +-- causeType
// |   +-- fromCancellation(canceled, callerToken)
// |   +-- fromCaught(exception, callerToken)
// |   +-- fromException(exception, userMessage, category)
// |   +-- fromExceptions(exceptionOrExceptions, userMessage, category)
// |   +-- fromMessage(message, category)
// |   +-- fromMessages(userMessage, technicalMessage, category, causeType)
// |   +-- hasStackTrace
// |   +-- message
// |   +-- new(userMessage, technicalMessage, category, stackTrace, causeType)
// |   +-- stackTrace
// |   +-- technicalMessage
// |   `-- userMessage
// `-- TIMED_OUT_USER_MESSAGE

import { ErrorCategory } from "./ErrorCategory.js";

/** The user message applied when a caller supplies an exception or null message without a user message. */
export const DEFAULT_USER_MESSAGE = "An unexpected error occurred";

/** The user message every cancellation error carries. */
export const CANCELED_USER_MESSAGE = "The operation was cancelled.";

/** The user message a timeout carries. */
export const TIMED_OUT_USER_MESSAGE = "The operation timed out.";

const MISSING_EXCEPTION_DETAIL = "A failure was reported without any exception detail";
const MISSING_ERROR_DETAIL = "A failure was reported without any error";

const CATEGORY_MAP: Readonly<Record<string, ErrorCategory>> = {
  unexpected: ErrorCategory.Unexpected,
  validation: ErrorCategory.Validation,
  notfound: ErrorCategory.NotFound,
  conflict: ErrorCategory.Conflict,
  forbidden: ErrorCategory.Forbidden,
  unavailable: ErrorCategory.Unavailable,
  canceled: ErrorCategory.Canceled,
  cancelled: ErrorCategory.Canceled,
  indeterminate: ErrorCategory.Indeterminate,
};

function resolveCategory(category?: ErrorCategory | string | null): ErrorCategory {
  if (typeof category !== "string") return ErrorCategory.Unexpected;
  const normalized = category.trim().toLowerCase().replaceAll("_", "");
  return CATEGORY_MAP[normalized] ?? ErrorCategory.Unexpected;
}

function getCauseType(exc: unknown): string {
  if (exc === null || exc === undefined) return String(exc);
  if (typeof exc === "object") {
    const obj = exc as { readonly name?: unknown; readonly constructor?: { readonly name?: string } };
    if (typeof obj.name === "string" && obj.name.length > 0 && obj.name !== "Error" && obj.name !== "DOMException") return obj.name;
    const ctorName = obj.constructor?.name;
    if (typeof ctorName === "string" && ctorName.length > 0 && ctorName !== "Object" && ctorName !== "Error") return ctorName;
    if (typeof obj.name === "string" && obj.name.length > 0) return obj.name;
    if (typeof ctorName === "string" && ctorName.length > 0) return ctorName;
  }
  return typeof exc;
}

function getExceptionMessage(exc: unknown): string {
  if (exc === null || exc === undefined) return "";
  if (typeof exc === "string") return exc;
  if (typeof exc === "object" && "message" in exc) {
    const msg = (exc as { readonly message?: unknown }).message;
    if (typeof msg === "string") return msg;
  }
  return String(exc);
}

function nextCause(current: object): unknown {
  if ("cause" in current) return (current as { readonly cause?: unknown }).cause;
  if ("innerException" in current) return (current as { readonly innerException?: unknown }).innerException;
  return undefined;
}

function describeException(exc: unknown): string {
  const chain: string[] = [];
  let current: unknown = exc;
  const seen = new Set<unknown>();
  while (current !== null && current !== undefined && !seen.has(current)) {
    seen.add(current);
    const typeName = getCauseType(current);
    const msg = getExceptionMessage(current);
    chain.push(msg.length > 0 ? `${typeName}: ${msg}` : typeName);
    current = typeof current === "object" ? nextCause(current) : undefined;
  }
  return chain.join(" ---> ");
}

function captureStackTrace(exc: unknown): string | null {
  const parts: string[] = [];
  let current: unknown = exc;
  const seen = new Set<unknown>();
  let isFirst = true;
  while (current !== null && current !== undefined && !seen.has(current)) {
    seen.add(current);
    if (typeof current === "object" && "stack" in current) {
      const stack = (current as { readonly stack?: unknown }).stack;
      if (typeof stack === "string" && stack.trim().length > 0) {
        parts.push(isFirst ? stack.trim() : `--- inner exception: ${getCauseType(current)} ---\n${stack.trim()}`);
        isFirst = false;
      }
    }
    current = typeof current === "object" ? nextCause(current) : undefined;
  }
  return parts.length > 0 ? parts.join("\n") : null;
}

function isTokenCanceled(callerToken: unknown): boolean {
  if (callerToken === null || callerToken === undefined) return false;
  if (typeof callerToken === "boolean") return callerToken;
  if (typeof callerToken === "object") {
    const t = callerToken as Record<string, unknown>;
    if (typeof t["aborted"] === "boolean") return t["aborted"];
    if (typeof t["isCancellationRequested"] === "boolean") return t["isCancellationRequested"];
    if (typeof t["is_cancellation_requested"] === "boolean") return t["is_cancellation_requested"];
    if (typeof t["isSet"] === "function") return Boolean((t["isSet"] as () => unknown)());
    if (typeof t["is_set"] === "function") return Boolean((t["is_set"] as () => unknown)());
  }
  return false;
}

function isTimeoutError(exc: unknown): boolean {
  if (exc === null || exc === undefined || typeof exc !== "object") return false;
  const typeName = getCauseType(exc).toLowerCase();
  if (typeName === "timeouterror" || typeName === "timeoutexception") return true;
  const code = (exc as { readonly code?: unknown }).code;
  if (code === "ETIMEDOUT" || code === "UND_ERR_CONNECT_TIMEOUT") return true;
  return "cause" in exc && isTimeoutError((exc as { readonly cause?: unknown }).cause);
}

function isCancellationOrTimeoutError(exc: unknown): boolean {
  if (exc === null || exc === undefined || typeof exc !== "object") return false;
  if (isTimeoutError(exc)) return true;
  const typeName = getCauseType(exc).toLowerCase();
  return (
    typeName === "aborterror" ||
    typeName === "cancelederror" ||
    typeName === "cancellederror" ||
    typeName === "operationcanceledexception" ||
    typeName === "taskcanceledexception"
  );
}

function extractResultExceptionErrors(exc: unknown): readonly Error[] | null {
  if (exc !== null && typeof exc === "object" && "errors" in exc) {
    const candidate = exc as { readonly name?: unknown; readonly errors?: unknown };
    if (
      (candidate.name === "ResultException" || getCauseType(exc) === "ResultException") &&
      Array.isArray(candidate.errors)
    ) {
      const valid = candidate.errors.filter((item): item is Error => item instanceof Error);
      if (valid.length > 0) return Object.freeze(valid);
    }
  }
  return null;
}

/**
 * Error is an immutable description of a single failure, carrying a user-safe message, a
 * technical message, a failure category, and captured stack trace and cause type without
 * retaining live external exception references.
 */
export class Error {
  public static readonly DEFAULT_USER_MESSAGE = DEFAULT_USER_MESSAGE;
  public static readonly CANCELED_USER_MESSAGE = CANCELED_USER_MESSAGE;
  public static readonly TIMED_OUT_USER_MESSAGE = TIMED_OUT_USER_MESSAGE;
  public static readonly DefaultUserMessage = DEFAULT_USER_MESSAGE;
  public static readonly CanceledUserMessage = CANCELED_USER_MESSAGE;
  public static readonly TimedOutUserMessage = TIMED_OUT_USER_MESSAGE;

  public readonly userMessage: string;
  public readonly technicalMessage: string;
  public readonly category: ErrorCategory;
  public readonly stackTrace: string | null;
  public readonly causeType: string | null;

  public constructor(
    userMessage: string | null | undefined = DEFAULT_USER_MESSAGE,
    technicalMessage?: string | null,
    category: ErrorCategory | string = ErrorCategory.Unexpected,
    stackTrace: string | null = null,
    causeType: string | null = null
  ) {
    const resolvedUser = userMessage ?? DEFAULT_USER_MESSAGE;
    this.userMessage = resolvedUser;
    this.technicalMessage = technicalMessage ?? resolvedUser;
    this.category = resolveCategory(category);
    this.stackTrace = stackTrace;
    this.causeType = causeType;
    Object.freeze(this);
  }

  /** Gets the diagnostic technical message if non-empty, otherwise the user message. */
  public get message(): string {
    return this.technicalMessage.length > 0 ? this.technicalMessage : this.userMessage;
  }

  /** Returns true when a non-empty stack trace was captured. */
  public get hasStackTrace(): boolean {
    return Boolean(this.stackTrace && this.stackTrace.trim().length > 0);
  }

  /** Creates an error whose single message serves both user and technical contexts. */
  public static fromMessage(
    message: string | null | undefined = undefined,
    category: ErrorCategory | string = ErrorCategory.Unexpected
  ): Error {
    const resolved = message ?? DEFAULT_USER_MESSAGE;
    return new Error(resolved, resolved, category, null, null);
  }

  /** Creates an error with distinct user-facing and technical diagnostic messages. */
  public static fromMessages(
    userMessage: string | null | undefined = undefined,
    technicalMessage: string | null | undefined = undefined,
    category: ErrorCategory | string = ErrorCategory.Unexpected,
    causeType: string | null = null
  ): Error {
    const userRes = userMessage ?? DEFAULT_USER_MESSAGE;
    const techRes = technicalMessage ?? userRes;
    return new Error(userRes, techRes, category, null, causeType);
  }

  /** Creates an error capturing message chain, stack trace, and causeType from an exception. */
  public static fromException(
    exception: unknown,
    userMessage: string | null | undefined = DEFAULT_USER_MESSAGE,
    category: ErrorCategory | string = ErrorCategory.Unexpected
  ): Error {
    const resolvedUser = userMessage ?? DEFAULT_USER_MESSAGE;
    if (exception === null || exception === undefined) {
      return new Error(resolvedUser, MISSING_EXCEPTION_DETAIL, category, null, null);
    }
    return new Error(
      resolvedUser,
      describeException(exception),
      category,
      captureStackTrace(exception),
      getCauseType(exception)
    );
  }

  /** Expands an exception, AggregateError, ResultException, or iterable of exceptions into flat structured errors. */
  public static fromExceptions(
    exceptionOrExceptions: unknown,
    userMessage: string | null | undefined = DEFAULT_USER_MESSAGE,
    category: ErrorCategory | string = ErrorCategory.Unexpected
  ): readonly Error[] {
    const resolvedUser = userMessage ?? DEFAULT_USER_MESSAGE;
    const resolvedCategory = resolveCategory(category);
    if (exceptionOrExceptions === null || exceptionOrExceptions === undefined) {
      return Object.freeze([Error.fromException(null, resolvedUser, resolvedCategory)]);
    }
    const resultErrors = extractResultExceptionErrors(exceptionOrExceptions);
    if (resultErrors !== null) return resultErrors;

    if (exceptionOrExceptions instanceof AggregateError) {
      const accumulated: Error[] = [];
      for (const nested of exceptionOrExceptions.errors) {
        accumulated.push(...Error.fromExceptions(nested, resolvedUser, resolvedCategory));
      }
      return accumulated.length > 0
        ? Object.freeze(accumulated)
        : Object.freeze([Error.fromException(exceptionOrExceptions, resolvedUser, resolvedCategory)]);
    }

    if (
      typeof exceptionOrExceptions === "object" &&
      !(exceptionOrExceptions instanceof globalThis.Error) &&
      Symbol.iterator in exceptionOrExceptions
    ) {
      const collected: Error[] = [];
      for (const item of exceptionOrExceptions as Iterable<unknown>) {
        collected.push(...Error.fromExceptions(item, resolvedUser, resolvedCategory));
      }
      return collected.length > 0
        ? Object.freeze(collected)
        : Object.freeze([Error.fromException(null, resolvedUser, resolvedCategory)]);
    }
    return Object.freeze([Error.fromException(exceptionOrExceptions, resolvedUser, resolvedCategory)]);
  }

  /** Creates a cancellation error carrying ErrorCategory.Canceled. */
  public static canceled(canceled: unknown = null): Error {
    const hasDetail = canceled !== null && canceled !== undefined;
    const tech = `The operation was cancelled before it finished: ${hasDetail ? getCauseType(canceled) : MISSING_EXCEPTION_DETAIL}`;
    return new Error(
      CANCELED_USER_MESSAGE,
      tech,
      ErrorCategory.Canceled,
      hasDetail ? captureStackTrace(canceled) : null,
      hasDetail ? getCauseType(canceled) : null
    );
  }

  /** Classifies a cancellation by caller evidence versus timeout. */
  public static fromCancellation(canceled: unknown = null, callerToken: unknown = null): Error {
    if (canceled === null || canceled === undefined) return Error.canceled(null);
    if (isTokenCanceled(callerToken)) return Error.canceled(canceled);
    if (isTimeoutError(canceled) || (callerToken !== null && callerToken !== undefined)) {
      return Error.fromException(canceled, TIMED_OUT_USER_MESSAGE, ErrorCategory.Unavailable);
    }
    return Error.canceled(canceled);
  }

  /** Translates any caught exception at an explicit adapter boundary. */
  public static fromCaught(exception: unknown, callerToken: unknown = null): readonly Error[] {
    if (exception === null || exception === undefined) {
      return Object.freeze([Error.fromException(null)]);
    }
    const resultErrors = extractResultExceptionErrors(exception);
    if (resultErrors !== null) return resultErrors;
    if (isCancellationOrTimeoutError(exception) || isTokenCanceled(callerToken)) {
      return Object.freeze([Error.fromCancellation(exception, callerToken)]);
    }
    return Error.fromExceptions(exception);
  }

  /** Normalizes an error sequence, substituting a sentinel error if null or empty. */
  public static atLeastOne(errors?: Iterable<Error | null | undefined> | null): readonly Error[] {
    const valid: Error[] = [];
    if (errors !== null && errors !== undefined) {
      for (const err of errors) {
        if (err !== null && err !== undefined) valid.push(err);
      }
    }
    return valid.length > 0
      ? Object.freeze(valid)
      : Object.freeze([new Error(DEFAULT_USER_MESSAGE, MISSING_ERROR_DETAIL, ErrorCategory.Unexpected, null, null)]);
  }
}
