// Responsibility: Contract for executing OAuth 2.0 Authorization Code + PKCE and refresh token exchanges.
// Public interface:
// `-- IOAuthFlowClient
//     +-- authorizeCode(request, signal)
//     `-- refreshToken(request, signal)

import { type Result } from "../results/Result.js";

/**
 * IOAuthFlowClient abstracts the loopback redirect capture and HTTPS token endpoint exchanges.
 */
export interface IOAuthFlowClient {
  /**
   * Executes the interactive authorization code + PKCE exchange and returns the issued tokens.
   */
  authorizeCode(
    request: {
      readonly authorizationEndpoint: string;
      readonly tokenEndpoint: string;
      readonly clientId: string;
      readonly scopes: ReadonlyArray<string>;
      readonly codeVerifier: string;
      readonly codeChallenge: string;
      readonly codeChallengeMethod: "S256";
      readonly state: string;
      readonly redirectPort?: number | undefined;
    },
    signal?: AbortSignal
  ): Promise<
    Result<{
      readonly accessToken: string;
      readonly refreshToken: string;
      readonly expiresInSeconds: number;
      readonly returnedState?: string | undefined;
    }>
  >;

  /**
   * Exchanges a refresh token for a fresh access token (and optional rotated refresh token).
   */
  refreshToken(
    request: {
      readonly tokenEndpoint: string;
      readonly clientId: string;
      readonly refreshToken: string;
      readonly scopes: ReadonlyArray<string>;
    },
    signal?: AbortSignal
  ): Promise<
    Result<{
      readonly accessToken: string;
      readonly refreshToken?: string | undefined;
      readonly expiresInSeconds: number;
    }>
  >;
}
