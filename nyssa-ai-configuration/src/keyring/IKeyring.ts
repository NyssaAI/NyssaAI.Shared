// Responsibility: Contract for storing and retrieving the application master encryption key.
// Public interface:
// +-- IKeyring
// |   +-- get(service, account, signal?)
// |   `-- set(service, account, secret, signal?)

import { type Result } from "../results/Result.js";

/**
 * IKeyring manages secure storage for the application's master cryptographic key.
 */
export interface IKeyring {
  /**
   * Retrieves the stored key for the named service and account.
   */
  get(service: string, account: string, signal?: AbortSignal): Promise<Result<Buffer>>;

  /**
   * Stores the key for the named service and account.
   */
  set(service: string, account: string, secret: Buffer, signal?: AbortSignal): Promise<Result<void>>;
}
