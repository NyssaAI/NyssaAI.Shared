// Responsibility: Register a validated email in PostgreSQL and own its correlated operational outcome.
// Public interface:
// `-- registerEmail(connectionString, logger, email, correlationId, signal, monotonicNow)
import pg from 'pg';
import {toEmailAddress} from '../email/EmailAddress.js';
import type {RegistrationLog} from './RegistrationLog.js';
import type {RegistrationResult} from './RegistrationResult.js';

/** Register once using an owned, bounded connection. Abort rejects with the caller's original reason.
 * Cancellation or transport failure after a write can leave the commit outcome uncertain; read before retrying.
 * The host owns the logger and deployment configuration; credentials and email never enter diagnostics.
 */
export async function registerEmail(connectionString: string, logger: RegistrationLog, email: string,
  correlationId: string, signal: AbortSignal, monotonicNow: () => number = () => performance.now()): Promise<RegistrationResult> {
  if (!/^[a-zA-Z0-9_-]{1,64}$/.test(correlationId)) throw new Error('Invalid correlation ID');
  const started = monotonicNow();
  let outcome: 'success' | 'rejected' | 'failure' | 'cancelled' = 'failure';
  let code: 'none' | 'email.invalid' | 'email.exists' | 'database.unavailable' | 'caller.cancelled' = 'database.unavailable';
  let usedDatabase = false;
  try {
    signal.throwIfAborted();
    const address = toEmailAddress(email);
    if (!address.ok) { outcome = 'rejected'; code = 'email.invalid'; return {ok: false, error: code}; }
    const client = new pg.Client({connectionString, connectionTimeoutMillis: 1500, query_timeout: 1500,
      statement_timeout: 1500, application_name: correlationId});
    // Closing the owned connection interrupts a pending query; the catch preserves the AbortSignal reason.
    const abort = (): void => { void client.end().catch(() => undefined); };
    client.on('error', () => { /* Query/connect rejection owns translation; idle socket errors must not escape. */ });
    signal.addEventListener('abort', abort, {once: true});
    try {
      usedDatabase = true;
      await client.connect();
      signal.throwIfAborted();
      const result = await client.query('INSERT INTO registrations (email) VALUES ($1) ON CONFLICT DO NOTHING', [address.value]);
      signal.throwIfAborted();
      if (result.rowCount === 0) { outcome = 'rejected'; code = 'email.exists'; return {ok: false, error: code}; }
      outcome = 'success'; code = 'none';
      return {ok: true};
    } catch (cause: unknown) {
      signal.throwIfAborted();
      return {ok: false, error: 'database.unavailable', cause};
    } finally {
      signal.removeEventListener('abort', abort);
      await client.end().catch(() => undefined);
    }
  } finally {
    if (signal.aborted) { outcome = 'cancelled'; code = 'caller.cancelled'; }
    const elapsedMs = Math.max(0, monotonicNow() - started);
    if (usedDatabase && elapsedMs > 250) logger.slow(correlationId, elapsedMs);
    logger.completed(correlationId, outcome, elapsedMs, code, usedDatabase ? 1 : 0);
  }
}
