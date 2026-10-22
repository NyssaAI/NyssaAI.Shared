// Responsibility: Represent success or a caller-actionable failure without exception control flow.
// Public interface:
// +-- Result
// |   +-- error
// |   +-- ok
// |   `-- value
// +-- fail(error)
// `-- ok(value)

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
export function fail<T = never>(error: string): Result<T> {
  return { ok: false, error };
}
