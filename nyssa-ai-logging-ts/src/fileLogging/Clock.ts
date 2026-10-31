// Responsibility: Time abstraction interface for log timestamps and retention calculations.
// Public interface:
// `-- Clock
//     `-- now()

export interface Clock {
  now(): Date;
}
