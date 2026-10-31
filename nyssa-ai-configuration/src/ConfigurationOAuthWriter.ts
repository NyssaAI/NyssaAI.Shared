// Responsibility: Public contract for interactive OAuth 2.0 PKCE credential acquisition and renewal.
// Public interface:
// `-- ConfigurationOAuthWriter
//     +-- authorize(namespace, key, options, signal)
//     +-- authorizeProvider(providerName, signal)
//     `-- reauthorize(namespace, key, signal)

import { type Result } from "./results/Result.js";
import type { OAuthOptions } from "./oauth/OAuthOptions.js";

/**
 * ConfigurationOAuthWriter acquires and renews OAuth 2.0 Authorization Code + PKCE grants.
 */
export interface ConfigurationOAuthWriter {
  /**
   * Acquires and stores an OAuth 2.0 Authorization Code + PKCE grant under the specified namespace and key.
   */
  authorize(
    namespace: string,
    key: string,
    options: OAuthOptions,
    signal?: AbortSignal
  ): Promise<Result<void>>;

  /**
   * Acquires and stores an OAuth 2.0 Authorization Code + PKCE grant for a configured cloud key vault provider.
   */
  authorizeProvider(providerName: string, signal?: AbortSignal): Promise<Result<void>>;

  /**
   * Re-runs interactive OAuth 2.0 PKCE sign-in using endpoint metadata already stored at (namespace, key).
   */
  reauthorize(namespace: string, key: string, signal?: AbortSignal): Promise<Result<void>>;
}
