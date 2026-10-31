// Responsibility: In-memory keyring implementation supporting simulated outage and write auditing for tests.
// Public interface:
// `-- InMemoryKeyring
//     +-- delete(service, account, signal)
//     +-- get(service, account, signal)
//     +-- set(service, account, secret, signal)
//     +-- setUnreachable(unreachable)
//     `-- writes

import { ok, fail, type Result } from "../results/Result.js";
import type { IKeyring } from "./IKeyring.js";

/**
 * InMemoryKeyring stores secrets in process memory for deterministic testing and headless composition.
 */
export class InMemoryKeyring {
  private readonly _entries = new Map<string, Buffer>();
  private readonly _writes: Array<{ readonly service: string; readonly account: string; readonly length: number }> = [];
  private _unreachable = false;

  /**
   * Recorded write operations for test assertions.
   */
  public get writes(): ReadonlyArray<{ readonly service: string; readonly account: string; readonly length: number }> {
    return this._writes;
  }

  /**
   * Simulates an unreachable or locked credential store when true.
   */
  public setUnreachable(unreachable: boolean): void {
    this._unreachable = unreachable;
  }

  public async get(service: string, account: string, signal?: AbortSignal): Promise<Result<Buffer | null>> {
    signal?.throwIfAborted();

    if (!service || service.trim().length === 0 || !account || account.trim().length === 0) {
      return fail("Keyring service and account must be non-empty.", "Validation");
    }

    if (this._unreachable) {
      return fail("The credential store is unreachable.", "Unavailable");
    }

    const entry = this._entries.get(`${service}/${account}`);
    return ok(entry ? Buffer.from(entry) : null);
  }

  public async set(service: string, account: string, secret: Buffer, signal?: AbortSignal): Promise<Result<void>> {
    signal?.throwIfAborted();

    if (!service || service.trim().length === 0 || !account || account.trim().length === 0) {
      return fail("Keyring service and account must be non-empty.", "Validation");
    }

    if (this._unreachable) {
      return fail("The credential store is unreachable.", "Unavailable");
    }

    this._entries.set(`${service}/${account}`, Buffer.from(secret));
    this._writes.push({ service, account, length: secret.length });
    return ok(undefined);
  }

  public async delete(service: string, account: string, signal?: AbortSignal): Promise<Result<void>> {
    signal?.throwIfAborted();

    if (!service || service.trim().length === 0 || !account || account.trim().length === 0) {
      return fail("Keyring service and account must be non-empty.", "Validation");
    }

    if (this._unreachable) {
      return fail("The credential store is unreachable.", "Unavailable");
    }

    this._entries.delete(`${service}/${account}`);
    return ok(undefined);
  }
}

const _typeCheck: IKeyring = null as unknown as InMemoryKeyring;
void _typeCheck;
