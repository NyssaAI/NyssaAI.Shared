// Responsibility: Manage OAuth 2.0 PKCE authorization, token expiry checks, silent refresh, and rotated grant durability.
// Public interface:
// `-- OAuthTokenManager
//     +-- REFRESH_WINDOW_MS
//     +-- authorize(namespace, key, options, signal)
//     +-- deletePendingGrant(namespace, key)
//     +-- isOAuthGrantPayload(decrypted)
//     +-- new(configurationDirectory, cipher, flowClient, browserLauncher, clock)
//     `-- resolveGrantAccessToken(namespace, key, entry, persistEntry, signal)

import * as fs from "node:fs";
import * as path from "node:path";
import * as crypto from "node:crypto";
import { ok, fail, type Result } from "../results/Result.js";
import { ProtectedFileSystem } from "../fileSecurity/ProtectedFileSystem.js";
import type { IConfigurationCipher } from "../encryption/IConfigurationCipher.js";
import type { ConfigurationValue } from "../values/ConfigurationValue.js";
import type { OAuthOptions } from "./OAuthOptions.js";
import type { IOAuthFlowClient } from "./IOAuthFlowClient.js";
import type { IBrowserLauncher } from "./IBrowserLauncher.js";

/**
 * OAuthTokenManager coordinates PKCE S256 sign-in, 60-second expiry window refresh,
 * invalid_grant marking, and crash-safe rotated grant persistence in .pending/<namespace>.oauth.json.
 */
export class OAuthTokenManager {
  public static readonly REFRESH_WINDOW_MS = 60000;

  private readonly _configurationDirectory: string;
  private readonly _cipher: IConfigurationCipher | undefined;
  private readonly _flowClient: IOAuthFlowClient | undefined;
  private readonly _browserLauncher: IBrowserLauncher | undefined;
  private readonly _clock: () => number;

  public constructor(
    configurationDirectory: string,
    cipher?: IConfigurationCipher,
    flowClient?: IOAuthFlowClient,
    browserLauncher?: IBrowserLauncher,
    clock?: () => number
  ) {
    this._configurationDirectory = path.resolve(configurationDirectory);
    this._cipher = cipher;
    this._flowClient = flowClient;
    this._browserLauncher = browserLauncher;
    this._clock = clock ?? (() => Date.now());
  }

  /**
   * Checks whether a decrypted string payload is a serialized StoredOAuthGrant.
   */
  public static isOAuthGrantPayload(decrypted: string): boolean {
    if (!decrypted.startsWith("{") || !decrypted.includes("\"__nyssa_oauth_grant\":true")) {
      return false;
    }
    try {
      const parsed = JSON.parse(decrypted) as { __nyssa_oauth_grant?: unknown };
      return parsed.__nyssa_oauth_grant === true;
    } catch {
      return false;
    }
  }

  /**
   * Runs the OAuth 2.0 Authorization Code + PKCE flow and returns the sealed ConfigurationValue entry.
   */
  public async authorize(
    namespace: string,
    key: string,
    options: OAuthOptions,
    signal?: AbortSignal
  ): Promise<Result<ConfigurationValue>> {
    signal?.throwIfAborted();

    const validRes = this.validateOptions(options);
    if (!validRes.ok) {
      return validRes;
    }

    if (!this._cipher) {
      return fail("Cannot store OAuth grant: no cipher configured.", "Unavailable");
    }

    if (!this._flowClient) {
      return fail("Signing in is not available: no IOAuthFlowClient configured.", "Validation");
    }

    const codeVerifier = crypto.randomBytes(32).toString("base64url");
    const codeChallenge = crypto.createHash("sha256").update(codeVerifier, "ascii").digest("base64url");
    const state = crypto.randomBytes(16).toString("base64url");
    const scopes = options.scopes ?? [];

    if (this._browserLauncher) {
      const url = new URL(options.authorizationEndpoint);
      url.searchParams.set("response_type", "code");
      url.searchParams.set("client_id", options.clientId);
      url.searchParams.set("code_challenge", codeChallenge);
      url.searchParams.set("code_challenge_method", "S256");
      url.searchParams.set("state", state);
      if (scopes.length > 0) {
        url.searchParams.set("scope", scopes.join(" "));
      }
      const launchRes = await this._browserLauncher.openUrl(url.toString(), signal);
      signal?.throwIfAborted();
      if (!launchRes.ok) {
        return launchRes;
      }
    }

    const exchangeRes = await this._flowClient.authorizeCode(
      {
        authorizationEndpoint: options.authorizationEndpoint,
        tokenEndpoint: options.tokenEndpoint,
        clientId: options.clientId,
        scopes,
        codeVerifier,
        codeChallenge,
        codeChallengeMethod: "S256",
        state,
        ...(options.redirectPort !== undefined ? { redirectPort: options.redirectPort } : {})
      },
      signal
    );
    signal?.throwIfAborted();

    if (!exchangeRes.ok) {
      return exchangeRes;
    }

    if (exchangeRes.value.returnedState !== undefined && exchangeRes.value.returnedState !== state) {
      return fail("oauth: state parameter mismatch on authorization callback", "Unauthorized");
    }

    const nowMs = this._clock();
    const grant = {
      __nyssa_oauth_grant: true as const,
      accessToken: exchangeRes.value.accessToken,
      refreshToken: exchangeRes.value.refreshToken,
      expiresAtMs: nowMs + exchangeRes.value.expiresInSeconds * 1000,
      tokenEndpoint: options.tokenEndpoint,
      authorizationEndpoint: options.authorizationEndpoint,
      clientId: options.clientId,
      scopes
    };

    const sealRes = this._cipher.seal(JSON.stringify(grant), namespace, key);
    if (!sealRes.ok) {
      return sealRes;
    }

    this.deletePendingGrant(namespace, key);

    return ok({
      value: sealRes.value,
      encrypted: true,
      provider: "aesLocal",
      reauth_required: false
    });
  }

  /**
   * Resolves the current access token for an OAuth grant entry, refreshing silently if within 60s of expiry
   * or recovering from a pending rotated grant in .pending/<namespace>.oauth.json.
   */
  public async resolveGrantAccessToken(
    namespace: string,
    key: string,
    entry: ConfigurationValue,
    persistEntry: (updated: ConfigurationValue) => Promise<Result<void>>,
    signal?: AbortSignal
  ): Promise<Result<string>> {
    signal?.throwIfAborted();

    if (!this._cipher) {
      return fail("Cannot decrypt OAuth grant: no cipher configured.", "Unavailable");
    }

    // 1. Check if a pending rotated grant exists for (namespace, key)
    let activeEntry = entry;
    const pendingEntry = this.readPendingGrant(namespace, key);
    if (pendingEntry) {
      const publishPending = await persistEntry(pendingEntry);
      if (!publishPending.ok) {
        return publishPending;
      }
      this.deletePendingGrant(namespace, key);
      activeEntry = pendingEntry;
    }

    if (activeEntry.reauth_required === true) {
      return fail(
        `oauth: credential '${namespace}/${key}' requires interactive re-authorization; call authorize('${namespace}', '${key}', options).`,
        "Unauthorized"
      );
    }

    if (typeof activeEntry.value !== "string") {
      return fail(`Encrypted OAuth grant '${namespace}/${key}' must carry a string payload.`, "Validation");
    }

    const openRes = this._cipher.open(activeEntry.value, namespace, key);
    if (!openRes.ok) {
      return openRes;
    }

    let grant: {
      readonly __nyssa_oauth_grant: true;
      readonly accessToken: string;
      readonly refreshToken: string;
      readonly expiresAtMs: number;
      readonly tokenEndpoint: string;
      readonly authorizationEndpoint: string;
      readonly clientId: string;
      readonly scopes: ReadonlyArray<string>;
    };
    try {
      grant = JSON.parse(openRes.value) as typeof grant;
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err);
      return fail(`Failed to parse OAuth grant for '${namespace}/${key}': ${message}`, "Validation");
    }

    const nowMs = this._clock();
    if (grant.expiresAtMs > nowMs + OAuthTokenManager.REFRESH_WINDOW_MS) {
      return ok(grant.accessToken);
    }

    // Needs silent token refresh
    if (!this._flowClient) {
      return fail("Cannot refresh OAuth token: no IOAuthFlowClient configured.", "Unavailable");
    }

    const refreshRes = await this._flowClient.refreshToken(
      {
        tokenEndpoint: grant.tokenEndpoint,
        clientId: grant.clientId,
        refreshToken: grant.refreshToken,
        scopes: grant.scopes
      },
      signal
    );
    signal?.throwIfAborted();

    if (!refreshRes.ok) {
      if (refreshRes.error.includes("invalid_grant") || refreshRes.category === "Unauthorized") {
        const markedEntry: ConfigurationValue = {
          ...activeEntry,
          reauth_required: true
        };
        await persistEntry(markedEntry);
        return fail(
          `oauth: refresh token for '${namespace}/${key}' was rejected (invalid_grant); interactive re-authorization is required.`,
          "Unauthorized"
        );
      }
      return refreshRes;
    }

    const updatedGrant = {
      ...grant,
      accessToken: refreshRes.value.accessToken,
      refreshToken: refreshRes.value.refreshToken ?? grant.refreshToken,
      expiresAtMs: this._clock() + refreshRes.value.expiresInSeconds * 1000
    };

    const sealedRotated = this._cipher.seal(JSON.stringify(updatedGrant), namespace, key);
    if (!sealedRotated.ok) {
      return sealedRotated;
    }

    const rotatedEntry: ConfigurationValue = {
      value: sealedRotated.value,
      encrypted: true,
      provider: "aesLocal",
      reauth_required: false
    };

    // Write rotated grant to .pending/<namespace>.oauth.json BEFORE updating <namespace>.json
    const pendingRes = this.writePendingGrant(namespace, key, rotatedEntry);
    if (!pendingRes.ok) {
      return pendingRes;
    }

    const publishRes = await persistEntry(rotatedEntry);
    if (!publishRes.ok) {
      // Leave .pending/<namespace>.oauth.json intact so the next get() recovers without losing the rotated refresh token!
      return publishRes;
    }

    this.deletePendingGrant(namespace, key);
    return ok(updatedGrant.accessToken);
  }

  /**
   * Deletes any pending rotated OAuth grant file for the namespace/key.
   */
  public deletePendingGrant(namespace: string, key: string): void {
    const filePath = this.pendingFilePath(namespace);
    if (!fs.existsSync(filePath)) {
      return;
    }
    try {
      const raw = fs.readFileSync(filePath, "utf8");
      const parsed = JSON.parse(raw) as { readonly key?: string; readonly entry?: ConfigurationValue };
      if (parsed.key === key) {
        fs.unlinkSync(filePath);
      }
    } catch {
      try {
        fs.unlinkSync(filePath);
      } catch {
        // ignore
      }
    }
  }

  private pendingFilePath(namespace: string): string {
    return path.join(this._configurationDirectory, ".pending", `${namespace}.oauth.json`);
  }

  private readPendingGrant(namespace: string, key: string): ConfigurationValue | null {
    const filePath = this.pendingFilePath(namespace);
    if (!fs.existsSync(filePath)) {
      return null;
    }
    try {
      const raw = fs.readFileSync(filePath, "utf8");
      const parsed = JSON.parse(raw) as { readonly key?: string; readonly entry?: ConfigurationValue };
      if (parsed && parsed.key === key && parsed.entry) {
        return parsed.entry;
      }
      return null;
    } catch {
      return null;
    }
  }

  private writePendingGrant(namespace: string, key: string, entry: ConfigurationValue): Result<void> {
    const filePath = this.pendingFilePath(namespace);
    const record = { key, entry };
    const writeRes = ProtectedFileSystem.createFile(filePath, JSON.stringify(record, null, 2));
    if (!writeRes.ok) {
      return writeRes;
    }
    return ok(undefined);
  }

  private validateOptions(options: OAuthOptions): Result<void> {
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

  private isHttpsUrl(candidate: string): boolean {
    try {
      const url = new URL(candidate);
      return url.protocol === "https:";
    } catch {
      return false;
    }
  }
}
