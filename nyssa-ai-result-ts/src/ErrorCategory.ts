// Responsibility: Define categories classifying failure outcomes for railway-oriented results.
// Public interface:
// `-- ErrorCategory

/**
 * ErrorCategory separates the kinds of failure that callers handle differently, so a handler can
 * branch on the kind without inspecting message text or retaining a framework exception.
 */
export type ErrorCategory =
  | "Unexpected"
  | "Validation"
  | "NotFound"
  | "Conflict"
  | "Forbidden"
  | "Unavailable"
  | "Canceled"
  | "Indeterminate";

/**
 * Runtime values for ErrorCategory supporting both PascalCase (C# parity) and UPPER_CASE (Python parity).
 */
export const ErrorCategory = {
  Unexpected: "Unexpected",
  Validation: "Validation",
  NotFound: "NotFound",
  Conflict: "Conflict",
  Forbidden: "Forbidden",
  Unavailable: "Unavailable",
  Canceled: "Canceled",
  Indeterminate: "Indeterminate",
  UNEXPECTED: "Unexpected",
  VALIDATION: "Validation",
  NOT_FOUND: "NotFound",
  CONFLICT: "Conflict",
  FORBIDDEN: "Forbidden",
  UNAVAILABLE: "Unavailable",
  CANCELED: "Canceled",
  INDETERMINATE: "Indeterminate",
} as const;
