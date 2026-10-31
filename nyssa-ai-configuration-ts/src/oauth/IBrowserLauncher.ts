// Responsibility: Contract for launching the system browser during interactive OAuth 2.0 sign-in.
// Public interface:
// `-- IBrowserLauncher
//     `-- openUrl(url, signal)

import { type Result } from "../results/Result.js";

/**
 * IBrowserLauncher opens an HTTPS authorization URL in the user's browser.
 */
export interface IBrowserLauncher {
  /**
   * Launches the given HTTPS URL.
   */
  openUrl(url: string, signal?: AbortSignal): Promise<Result<void>>;
}
