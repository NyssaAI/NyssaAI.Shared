// Responsibility: Own an isolated Docker PostgreSQL dependency from acquisition through migration and cleanup.
// Public interface:
// `-- startPostgresFixture(failMigration, name)
import {execFile} from 'node:child_process';
import {promisify} from 'node:util';
import {randomUUID} from 'node:crypto';
import {readFile} from 'node:fs/promises';
import {setTimeout as delay} from 'node:timers/promises';
import pg from 'pg';

const EXEC_FILE = promisify(execFile);
const POSTGRES_IMAGE = 'postgres@sha256:18cfe3ef5e6815560c98237d6216d1e5119702fb0f3894c8785dd58b8bbe5d73';

/** Start a fresh real database and production migration. The optional fault/name exist only to test fixture cleanup.
 * All Docker calls are bounded. The fixture removes its container and anonymous volume even when setup fails.
 */
export async function startPostgresFixture(failMigration = false, name = `standards-ts-${randomUUID()}`) {
  const password = randomUUID();
  const docker = async (...args: string[]): Promise<string> => {
    try { return (await EXEC_FILE('docker', args, {timeout: 60000, maxBuffer: 65536})).stdout.trim(); }
    catch { throw new Error(`Docker fixture command failed: ${args[0]}`); }
  };
  const close = async (): Promise<void> => { await docker('rm', '-f', '-v', name); };
  try {
    // The name is allocated before create; catch can remove resources even if create fails after acquisition.
    await docker('create', '--name', name, '-e', `POSTGRES_PASSWORD=${password}`, '-e', 'POSTGRES_DB=registrations',
      '-p', '127.0.0.1::5432', POSTGRES_IMAGE);
    await docker('start', name);
    const port = (await docker('port', name, '5432/tcp')).split(':').at(-1);
    if (!port || !/^\d+$/.test(port)) throw new Error('Docker did not publish a database port');
    const connectionString = `postgres://postgres:${password}@127.0.0.1:${port}/registrations`;
    const deadline = performance.now() + 30000;
    while (true) {
      const probe = new pg.Client({connectionString, connectionTimeoutMillis: 500, query_timeout: 500});
      try { await probe.connect(); await probe.query('SELECT 1'); break; }
      catch { if (performance.now() >= deadline) throw new Error('PostgreSQL readiness deadline exceeded'); }
      finally { await probe.end(); }
      await delay(100);
    }
    const migration = await readFile(new URL('./001_registrations.sql', import.meta.url), 'utf8');
    const setup = new pg.Client({connectionString, connectionTimeoutMillis: 1500, query_timeout: 1500});
    try {
      await setup.connect();
      await setup.query(failMigration ? 'INVALID MIGRATION' : migration);
    } finally { await setup.end(); }
    return {
      connectionString,
      close,
      stop: async (): Promise<void> => { await docker('stop', '--time', '1', name); },
      readEmails: async (): Promise<readonly string[]> => {
        const observer = new pg.Client({connectionString, connectionTimeoutMillis: 1500, query_timeout: 1500});
        try {
          await observer.connect();
          const result = await observer.query<{email: string}>('SELECT email FROM registrations ORDER BY email');
          return result.rows.map(row => row.email);
        } finally { await observer.end(); }
      }
    };
  } catch (cause: unknown) {
    try { await close(); }
    catch { throw new Error(`PostgreSQL fixture setup and cleanup failed; inspect container ${name}`); }
    throw new Error('PostgreSQL fixture setup failed; partial resources removed', {cause});
  }
}
