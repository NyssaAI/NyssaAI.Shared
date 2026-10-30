// Responsibility: Report email registration outcomes while retaining dependency causes privately for callers.
// Public interface:
// `-- RegistrationResult
//     +-- cause
//     +-- error
//     `-- ok
/** RegistrationResult preserves the original dependency cause without requiring exception control flow. */
export type RegistrationResult =
  | {/** Whether registration succeeded. */ readonly ok: true}
  | {
      /** Whether registration succeeded. */ readonly ok: false;
      /** Stable caller-actionable reason. */ readonly error: 'email.invalid' | 'email.exists' | 'database.unavailable';
      /** Original dependency failure; never serialize it into logs or responses. */ readonly cause?: unknown;
    };
