// Responsibility: Encode, decode, and validate OAuth 2.0 PKCE options and sealed grant payloads with C# and Python wire parity.
// Public interface:
// `-- OAuthGrantCodec
//     +-- buildAuthorizeUrl(options, codeChallenge, state)
//     +-- decodeGrant(decrypted, namespace, key)
//     +-- encodeGrant(grant)
//     +-- isOAuthGrantPayload(decrypted)
//     `-- validateOptions(options)

import { ok, fail, type Result } from "../results/Result.js";
import type { OAuthOptions } from "./OAuthOptions.js";

/**
 * OAuthGrantCodec serializes and deserializes OAuth grants so C#, Python, and TypeScript
 * can read and refresh grants written by any of the three runtimes.
 */
export class OAuthGrantCodec {
  public static isOAuthGrantPayload(decrypted: string): boolean {
    if (!decrypted.startsWith("{")) {
      return false;
    }
    if (
      !decrypted.includes("\"__nyssa_oauth_grant\"") &&
      !decrypted.includes("\"AccessToken\"") &&
      !decrypted.includes("\"accessToken\"")
    ) {
      return false;
    }
    try {
      const parsed = JSON.parse(decrypted) as Record<string, unknown>;
      if (parsed["__nyssa_oauth_grant"] === true) {
        return true;
      }
      const hasAccess =
        typeof parsed["AccessToken"] === "string" || typeof parsed["accessToken"] === "string";
      const hasEndpoint =
        typeof parsed["TokenEndpoint"] === "string" || typeof parsed["tokenEndpoint"] === "string";
      return hasAccess && hasEndpoint;
    } catch {
      return false;
    }
  }

  public static encodeGrant(grant: {
    readonly accessToken: string;
    readonly refreshToken: string;
    readonly expiresAtMs: number;
    readonly tokenEndpoint: string;
    readonly authorizationEndpoint: string;
    readonly clientId: string;
    readonly scopes: ReadonlyArray<string>;
    readonly redirectPort?: number | undefined;
  }): string {
    const expiresAtIso = new Date(grant.expiresAtMs).toISOString();
    return JSON.stringify({
      __nyssa_oauth_grant: true,
      AccessToken: grant.accessToken,
      RefreshToken: grant.refreshToken,
      TokenType: "Bearer",
      ExpiresAt: expiresAtIso,
      AuthorizationEndpoint: grant.authorizationEndpoint,
      TokenEndpoint: grant.tokenEndpoint,
      ClientId: grant.clientId,
      Scopes: grant.scopes,
      RedirectPort: grant.redirectPort ?? null,
      accessToken: grant.accessToken,
      refreshToken: grant.refreshToken,
      expiresAtMs: grant.expiresAtMs,
      tokenEndpoint: grant.tokenEndpoint,
      authorizationEndpoint: grant.authorizationEndpoint,
      clientId: grant.clientId,
      scopes: grant.scopes
    });
  }

  public static decodeGrant(
    decrypted: string,
    namespace: string,
    key: string
  ): Result<{
    readonly accessToken: string;
    readonly refreshToken: string;
    readonly expiresAtMs: number;
    readonly tokenEndpoint: string;
    readonly authorizationEndpoint: string;
    readonly clientId: string;
    readonly scopes: ReadonlyArray<string>;
    readonly redirectPort?: number | undefined;
  }> {
    try {
      const raw = JSON.parse(decrypted) as Record<string, unknown>;
      const accessToken =
        typeof raw["accessToken"] === "string"
          ? raw["accessToken"]
          : typeof raw["AccessToken"] === "string"
            ? raw["AccessToken"]
            : "";
      const refreshToken =
        typeof raw["refreshToken"] === "string"
          ? raw["refreshToken"]
          : typeof raw["RefreshToken"] === "string"
            ? raw["RefreshToken"]
            : "";
      const tokenEndpoint =
        typeof raw["tokenEndpoint"] === "string"
          ? raw["tokenEndpoint"]
          : typeof raw["TokenEndpoint"] === "string"
            ? raw["TokenEndpoint"]
            : "";
      const authorizationEndpoint =
        typeof raw["authorizationEndpoint"] === "string"
          ? raw["authorizationEndpoint"]
          : typeof raw["AuthorizationEndpoint"] === "string"
            ? raw["AuthorizationEndpoint"]
            : "";
      const clientId =
        typeof raw["clientId"] === "string"
          ? raw["clientId"]
          : typeof raw["ClientId"] === "string"
            ? raw["ClientId"]
            : "";

      let expiresAtMs = 0;
      if (typeof raw["expiresAtMs"] === "number") {
        expiresAtMs = raw["expiresAtMs"];
      } else if (typeof raw["ExpiresAt"] === "string") {
        expiresAtMs = Date.parse(raw["ExpiresAt"]);
      } else if (typeof raw["expiresAt"] === "string") {
        expiresAtMs = Date.parse(raw["expiresAt"]);
      }

      const rawScopes = Array.isArray(raw["scopes"])
        ? raw["scopes"]
        : Array.isArray(raw["Scopes"])
          ? raw["Scopes"]
          : [];
      const scopes = rawScopes.filter((s): s is string => typeof s === "string");
      const redirectPort =
        typeof raw["redirectPort"] === "number"
          ? raw["redirectPort"]
          : typeof raw["RedirectPort"] === "number"
            ? raw["RedirectPort"]
            : undefined;

      return ok({
        accessToken,
        refreshToken,
        expiresAtMs,
        tokenEndpoint,
        authorizationEndpoint,
        clientId,
        scopes,
        ...(redirectPort !== undefined ? { redirectPort } : {})
      });
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err);
      return fail(`Failed to parse OAuth grant for '${namespace}/${key}': ${message}`, "Validation");
    }
  }

  public static buildAuthorizeUrl(
    options: OAuthOptions,
    codeChallenge: string,
    state: string
  ): string {
    const url = new URL(options.authorizationEndpoint);
    url.searchParams.set("response_type", "code");
    url.searchParams.set("client_id", options.clientId);
    url.searchParams.set("code_challenge", codeChallenge);
    url.searchParams.set("code_challenge_method", "S256");
    url.searchParams.set("state", state);
    const scopes = options.scopes ?? [];
    if (scopes.length > 0) {
      url.searchParams.set("scope", scopes.join(" "));
    }
    return url.toString();
  }

  public static validateOptions(options: OAuthOptions): Result<void> {
    if (!options) {
      return fail("oauth: options are required", "Validation");
    }
    if (!this.isHttpsUrl(options.authorizationEndpoint)) {
      return fail(
        `oauth: the authorization endpoint must be an absolute https URI; '${options.authorizationEndpoint}' was supplied`,
        "Validation"
      );
    }
    if (!this.isHttpsUrl(options.tokenEndpoint)) {
      return fail(
        `oauth: the token endpoint must be an absolute https URI; '${options.tokenEndpoint}' was supplied`,
        "Validation"
      );
    }
    if (!options.clientId || options.clientId.trim().length === 0) {
      return fail("oauth: a client id is required", "Validation");
    }
    if (
      options.redirectPort !== undefined &&
      (!Number.isInteger(options.redirectPort) || options.redirectPort < 1 || options.redirectPort > 65535)
    ) {
      return fail(
        `oauth: redirect port ${options.redirectPort} is not between 1 and 65535`,
        "Validation"
      );
    }
    return ok(undefined);
  }

  private static isHttpsUrl(candidate: string): boolean {
    try {
      const url = new URL(candidate);
      return url.protocol === "https:";
    } catch {
      return false;
    }
  }
}
