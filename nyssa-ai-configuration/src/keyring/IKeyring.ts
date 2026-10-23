// Responsibility: Contract for storing, retrieving, and deleting the application master encryption key.
// Public interface:
// `-- IKeyring
//     +-- delete(service, account, signal)
//     +-- get(service, account, signal)
//     `-- set(service, account, secret, signal)

import { type Result } from "../results/Result.js";

/**
 * IKeyring manages secure storage for the application's master cryptographic key.
 */
export interface IKeyring {
  /**
   * Retrieves the stored key for the named service and account, or null when no key is filed.
   */
  get(service: string, account: string, signal?: AbortSignal): Promise<Result<Buffer | null>>;

  /**
   * Stores the key for the named service and account.
   */
  set(service: string, account: string, secret: Buffer, signal?: AbortSignal): Promise<Result<void>>;

  /**
   * Deletes the stored key for the named service and account. Succeeds when already absent.
   */
  delete(service: string, account: string, signal?: AbortSignal): Promise<Result<void>>;
}
