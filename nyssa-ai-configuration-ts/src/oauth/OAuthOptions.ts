// Responsibility: Caller-supplied OAuth 2.0 Authorization Code + PKCE parameters.
// Public interface:
// `-- OAuthOptions
//     +-- authority
//     +-- authorizationEndpoint
//     +-- clientId
//     +-- redirectPort
//     +-- scopes
//     `-- tokenEndpoint

/**
 * OAuthOptions configures an OAuth 2.0 Authorization Code + PKCE flow for a public client.
 */
export interface OAuthOptions {
  /** Optional authority base URL. */
  readonly authority?: string | undefined;
  /** The HTTPS authorization endpoint where the user's browser is directed. */
  readonly authorizationEndpoint: string;
  /** The HTTPS token endpoint where authorization codes and refresh tokens are exchanged. */
  readonly tokenEndpoint: string;
  /** The public OAuth client identifier. */
  readonly clientId: string;
  /** Requested scopes, joined with spaces on the wire. */
  readonly scopes?: ReadonlyArray<string> | undefined;
  /** Optional loopback redirect port (1..65535); an ephemeral port is used when omitted. */
  readonly redirectPort?: number | undefined;
}
