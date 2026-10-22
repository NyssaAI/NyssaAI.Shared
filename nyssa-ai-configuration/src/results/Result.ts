// Responsibility: Represent success or a caller-actionable failure without exception control flow.
// Public interface:
// +-- Result
// |   +-- category
// |   +-- error
// |   +-- ok
// |   +-- recoveryHandle
// |   `-- value
// +-- fail(error, category, recoveryHandle)
// `-- ok(value)

import type { ConfigurationWriteRecoveryHandle } from "../recovery/ConfigurationWriteRecoveryHandle.js";

/**
 * Result holds exactly one outcome; narrow ok before accessing value or error.
 */
export type Result<T> =
  | {
      /** ok selects the successful arm. */
      readonly ok: true;
      /** value is the successfully produced data. */
      readonly value: T;
    }
  | {
      /** ok selects the failed arm. */
      readonly ok: false;
      /** error is this sample's stable caller-actionable validation code or message. */
      readonly error: string;
      /** category classifies the failure for structured caller handling. */
      readonly category?:
        | "NotFound"
        | "Validation"
        | "Conflict"
        | "Unauthorized"
        | "Forbidden"
        | "Unavailable"
        | "Unexpected";
      /** recoveryHandle is attached when a multi-step write failed after recording a recovery journal. */
      readonly recoveryHandle?: ConfigurationWriteRecoveryHandle;
    };

/**
 * ok wraps a successful value in a Result.
 */
export function ok<T>(value: T): Result<T> {
  return { ok: true, value };
}

/**
 * fail carries a stable failure message or code in a Result.
 */
export function fail<T = never>(
  error: string,
  category?:
    | "NotFound"
    | "Validation"
    | "Conflict"
    | "Unauthorized"
    | "Forbidden"
    | "Unavailable"
    | "Unexpected",
  recoveryHandle?: ConfigurationWriteRecoveryHandle
): Result<T> {
  return {
    ok: false,
    error,
    ...(category !== undefined ? { category } : {}),
    ...(recoveryHandle !== undefined ? { recoveryHandle } : {})
  };
}
