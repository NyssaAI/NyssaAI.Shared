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
import { OAuthGrantCodec } from "./OAuthGrantCodec.js";
import type { IConfigurationCipher } from "../encryption/IConfigurationCipher.js";
import type { ConfigurationValue } from "../values/ConfigurationValue.js";
import type { OAuthOptions } from "./OAuthOptions.js";
import type { IOAuthFlowClient } from "./IOAuthFlowClient.js";
import type { IBrowserLauncher } from "./IBrowserLauncher.js";

/**
 * OAuthTokenManager coordinates PKCE S256 sign-in, 5-minute expiry window refresh,
 * invalid_grant marking, and rotated grant durability.
 */
export class OAuthTokenManager {
  public static readonly REFRESH_WINDOW_MS = 300000;

  private readonly _configurationDirectory: string;
  private readonly _cipher: IConfigurationCipher | undefined;
  private readonly _flowClient: IOAuthFlowClient | undefined;
  private readonly _browserLauncher: IBrowserLauncher | undefined;
  private readonly _clock: () => number;
  private readonly _heldGrants = new Map<string, ConfigurationValue>();

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

  public static isOAuthGrantPayload(decrypted: string): boolean {
    return OAuthGrantCodec.isOAuthGrantPayload(decrypted);
  }

  public async authorize(
    namespace: string,
    key: string,
    options: OAuthOptions,
    signal?: AbortSignal
  ): Promise<Result<ConfigurationValue>> {
    signal?.throwIfAborted();

    const validRes = OAuthGrantCodec.validateOptions(options);
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
      const authUrl = OAuthGrantCodec.buildAuthorizeUrl(options, codeChallenge, state);
      const launchRes = await this._browserLauncher.openUrl(authUrl, signal);
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
    const payload = OAuthGrantCodec.encodeGrant({
      accessToken: exchangeRes.value.accessToken,
      refreshToken: exchangeRes.value.refreshToken,
      expiresAtMs: nowMs + exchangeRes.value.expiresInSeconds * 1000,
      tokenEndpoint: options.tokenEndpoint,
      authorizationEndpoint: options.authorizationEndpoint,
      clientId: options.clientId,
      scopes,
      redirectPort: options.redirectPort
    });

    const sealRes = this._cipher.seal(payload, namespace, key);
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

    let activeEntry = entry;
    const pendingEntry = this._heldGrants.get(`${namespace}/${key}`) ?? this.readPendingGrant(namespace, key);
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

    const grantRes = OAuthGrantCodec.decodeGrant(openRes.value, namespace, key);
    if (!grantRes.ok) {
      return grantRes;
    }
    const grant = grantRes.value;

    const nowMs = this._clock();
    if (grant.expiresAtMs > nowMs + OAuthTokenManager.REFRESH_WINDOW_MS) {
      return ok(grant.accessToken);
    }

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

    const updatedAccessToken = refreshRes.value.accessToken;
    const updatedPayload = OAuthGrantCodec.encodeGrant({
      ...grant,
      accessToken: updatedAccessToken,
      refreshToken: refreshRes.value.refreshToken ?? grant.refreshToken,
      expiresAtMs: this._clock() + refreshRes.value.expiresInSeconds * 1000
    });

    const sealedRotated = this._cipher.seal(updatedPayload, namespace, key);
    if (!sealedRotated.ok) {
      return sealedRotated;
    }

    const rotatedEntry: ConfigurationValue = {
      value: sealedRotated.value,
      encrypted: true,
      provider: activeEntry.provider ?? "aesLocal",
      reauth_required: false
    };

    this._heldGrants.set(`${namespace}/${key}`, rotatedEntry);
    const pendingRes = this.writePendingGrant(namespace, key, rotatedEntry);
    if (!pendingRes.ok) {
      return pendingRes;
    }

    const publishRes = await persistEntry(rotatedEntry);
    if (!publishRes.ok) {
      return publishRes;
    }

    this.deletePendingGrant(namespace, key);
    return ok(updatedAccessToken);
  }

  public deletePendingGrant(namespace: string, key: string): void {
    this._heldGrants.delete(`${namespace}/${key}`);
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
}
