// Responsibility: Serialize allowlisted registration outcomes and contain synchronous diagnostic sink failures.
// Public interface:
// `-- RegistrationLog
//     +-- completed(correlationId, outcome, elapsedMs, code, attempts)
//     +-- lostRecords
//     +-- new(sink, version, now)
//     `-- slow(correlationId, elapsedMs)
/** RegistrationLog borrows a synchronous, nonblocking host sink; it owns no buffers or flush lifecycle. */
export class RegistrationLog {
  private lost = 0;
  /** Construct with a host sink, bounded deployment label and injectable UTC clock. The sink must return promptly. */
  constructor(private readonly sink: (line: string) => void, private readonly version: string,
    private readonly now: () => Date = () => new Date()) {
    if (!/^[a-zA-Z0-9._-]{1,64}$/.test(version)) throw new Error('Invalid deployment version');
  }
  /** Independent cumulative counter: hosts can inspect it even when the sink is unavailable. */
  get lostRecords(): number { return this.lost; }
  /** Emit exactly one terminal outcome; no arbitrary input, exception message, stack or nested cause is accepted. */
  completed(correlationId: string, outcome: 'success' | 'rejected' | 'failure' | 'cancelled',
    elapsedMs: number, code: 'none' | 'email.invalid' | 'email.exists' | 'database.unavailable' | 'caller.cancelled', attempts: 0 | 1): void {
    this.write('registration.completed', outcome === 'failure' ? 'error' : 'info', correlationId, outcome, elapsedMs, code, attempts);
  }
  /** Emit a warning for the sample's provisional 250 ms dependency threshold, including recovered successes. */
  slow(correlationId: string, elapsedMs: number): void {
    this.write('registration.slow', 'warn', correlationId, 'degraded', elapsedMs, 'dependency.slow', 1);
  }
  private write(event: string, level: string, correlationId: string, outcome: string, elapsedMs: number, code: string, attempts: 0 | 1): void {
    try {
      this.sink(JSON.stringify({timestamp: this.now().toISOString(), event, level, service: 'registration-example',
        version: this.version, operation: 'email.register', correlation_id: correlationId,
        outcome, elapsed_ms: elapsedMs, dependency: 'postgres', attempts, code}) + '\n');
    } catch { this.lost++; }
  }
}
