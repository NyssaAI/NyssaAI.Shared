// Responsibility: Represent success or a caller-actionable failure without exception control flow.
// Public interface:
// +-- Result
// |   +-- error
// |   +-- ok
// |   `-- value
// +-- fail(error)
// `-- ok(value)
/** Result holds exactly one outcome; narrow ok before accessing value or error. */
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
      /** error is this sample's stable caller-actionable validation code. */
      readonly error: string;
    };
/** ok wraps a successful value. */
export function ok<T>(value: T): Result<T> { return {ok: true, value}; }
/** fail carries a stable failure code. */
export function fail<T>(error: string): Result<T> { return {ok: false, error}; }
