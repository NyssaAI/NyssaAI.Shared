// Responsibility: Represent success or a caller-actionable failure without exception control flow.
// Public interface:
// +-- Result
// |   +-- category
// |   +-- error
// |   +-- ok
// |   +-- progress
// |   +-- recoveryHandle
// |   `-- value
// +-- fail(error, category, recoveryHandle, progress)
// `-- ok(value, progress, recoveryHandle)

import { failError, ok as sharedOk } from "nyssa-ai-result";
import type { ConfigurationRecoveryHandle } from "../recovery/ConfigurationRecoveryHandle.js";
import type { ConfigurationOperationProgress } from "../recovery/Types.js";

/**
 * Result holds exactly one outcome; narrow ok before accessing value or error.
 */
export type Result<T> =
  | {
      /** ok selects the successful arm. */
      readonly ok: true;
      /** value is the successfully produced data. */
      readonly value: T;
      /** progress is attached when a multi-step operation completes with remaining cleanup or step receipts. */
      readonly progress?: ConfigurationOperationProgress;
      /** recoveryHandle is attached when a multi-step operation has unfinished cleanup that can be resumed. */
      readonly recoveryHandle?: ConfigurationRecoveryHandle;
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
        | "Unsupported"
        | "Indeterminate"
        | "Unexpected";
      /** recoveryHandle is attached when a multi-step write failed after recording a guarded recovery token. */
      readonly recoveryHandle?: ConfigurationRecoveryHandle;
      /** progress is attached when a multi-step write failed after preparing step receipts. */
      readonly progress?: ConfigurationOperationProgress;
    };

/**
 * ok wraps a successful value in a Result.
 */
export function ok<T>(
  value: T,
  progress?: ConfigurationOperationProgress,
  recoveryHandle?: ConfigurationRecoveryHandle
): Result<T> {
  const base = sharedOk(value);
  if (progress === undefined && recoveryHandle === undefined) {
    return base;
  }
  return {
    ...base,
    ...(progress !== undefined ? { progress } : {}),
    ...(recoveryHandle !== undefined ? { recoveryHandle } : {})
  };
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
    | "Unsupported"
    | "Indeterminate"
    | "Unexpected",
  recoveryHandle?: ConfigurationRecoveryHandle,
  progress?: ConfigurationOperationProgress
): Result<T> {
  const base = failError(error);
  return {
    ok: base.ok,
    error: base.error,
    ...(category !== undefined ? { category } : {}),
    ...(recoveryHandle !== undefined ? { recoveryHandle } : {}),
    ...(progress !== undefined ? { progress } : {})
  };
}
