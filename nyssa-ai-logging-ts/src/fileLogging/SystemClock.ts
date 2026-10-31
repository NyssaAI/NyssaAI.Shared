// Responsibility: Default system clock implementation returning current UTC time.
// Public interface:
// `-- SystemClock
//     `-- now()

export class SystemClock {
  public now(): Date {
    return new Date();
  }
}
