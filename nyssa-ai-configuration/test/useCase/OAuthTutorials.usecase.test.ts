import { describe, it, expect, beforeEach, afterEach } from "vitest";
import * as fs from "node:fs";
import * as path from "node:path";
import * as os from "node:os";
import { ConfigurationClient } from "../../src/ConfigurationClient.js";
import { InMemoryKeyring } from "../../src/keyring/InMemoryKeyring.js";
import { ok, fail, type Result } from "../../src/results/Result.js";
import type { IBrowserLauncher } from "../../src/oauth/IBrowserLauncher.js";
import type { IOAuthFlowClient } from "../../src/oauth/IOAuthFlowClient.js";
import type { IVaultProvider } from "../../src/vault/IVaultProvider.js";
import type { IVaultProviderFactory } from "../../src/vault/IVaultProviderFactory.js";

describe("OAuthTutorials Public Use Cases (F-202)", () => {
  let tempDir: string;
  let manifestPath: string;
  let configDir: string;
  let keyring: InMemoryKeyring;

  beforeEach(() => {
    tempDir = fs.mkdtempSync(path.join(os.tmpdir(), "nyssa-oauth-tutorials-"));
    manifestPath = path.join(tempDir, "config-manifest.json");
    configDir = path.join(tempDir, "config");
    keyring = new InMemoryKeyring();
  });

  afterEach(() => {
    try {
      fs.rmSync(tempDir, { recursive: true, force: true });
    } catch {
      // ignore
    }
  });

  it("oauthTutorials_firstTimeAuthorizeAndSilentRefresh_verifiesPkceAndRefreshesWithoutBrowser (F-202 UC-001 & UC-002)", async () => {
    let browserLaunchCount = 0;
    let capturedCodeVerifier = "";
    let currentNowMs = Date.parse("2026-10-22T12:00:00Z");

    const browserLauncher: IBrowserLauncher = {
      async openUrl(url: string): Promise<Result<void>> {
        browserLaunchCount++;
        const parsed = new URL(url);
        const challenge = parsed.searchParams.get("code_challenge") ?? "";
        expect(parsed.searchParams.get("code_challenge_method")).toBe("S256");
        expect(challenge.length).toBeGreaterThan(20);
        return ok(undefined);
      }
    };

    const oauthClient: IOAuthFlowClient = {
      async authorizeCode(request): Promise<
        Result<{
          readonly accessToken: string;
          readonly refreshToken: string;
          readonly expiresInSeconds: number;
          readonly returnedState?: string | undefined;
        }>
      > {
        capturedCodeVerifier = request.codeVerifier;
        return ok({
          accessToken: "access-token-initial",
          refreshToken: "refresh-token-initial",
          expiresInSeconds: 3600,
          returnedState: request.state
        });
      },
      async refreshToken(request): Promise<
        Result<{
          readonly accessToken: string;
          readonly refreshToken?: string | undefined;
          readonly expiresInSeconds: number;
        }>
      > {
        expect(request.refreshToken).toBe("refresh-token-initial");
        return ok({
          accessToken: "access-token-refreshed",
          refreshToken: "refresh-token-rotated",
          expiresInSeconds: 3600
        });
      }
    };

    const clientRes = await ConfigurationClient.initialize({
      applicationName: "OAuthApp",
      configManifest: manifestPath,
      configDirectory: configDir,
      keyring,
      browserLauncher,
      oauthClient,
      clock: () => currentNowMs
    });
    expect(clientRes.ok).toBe(true);
    if (!clientRes.ok) return;
    const client = clientRes.value;

    // 1. First-time interactive PKCE setup
    const authRes = await client.authorize("graph", "mailToken", {
      authorizationEndpoint: "https://login.example.com/oauth2/v2.0/authorize",
      tokenEndpoint: "https://login.example.com/oauth2/v2.0/token",
      clientId: "client-id-123",
      scopes: ["mail.read", "offline_access"]
    });
    expect(authRes.ok).toBe(true);
    expect(browserLaunchCount).toBe(1);
    expect(capturedCodeVerifier.length).toBeGreaterThanOrEqual(43);

    // Verify encrypted tokens on disk
    const rawGraph = fs.readFileSync(path.join(configDir, "graph.json"), "utf8");
    expect(rawGraph).not.toContain("access-token-initial");
    expect(rawGraph).not.toContain("refresh-token-initial");

    // 2. Get mailToken while valid (no refresh, no browser)
    const validTokenRes = await client.get("graph", "mailToken");
    expect(validTokenRes.ok && validTokenRes.value).toBe("access-token-initial");
    expect(browserLaunchCount).toBe(1);

    // 3. Advance clock within 60-second expiry window (e.g., 59 minutes and 30 seconds later)
    currentNowMs = Date.parse("2026-10-22T12:59:30Z");
    const refreshedTokenRes = await client.get("graph", "mailToken");
    expect(refreshedTokenRes.ok && refreshedTokenRes.value).toBe("access-token-refreshed");
    expect(browserLaunchCount).toBe(1); // 0 additional browser launches
  });

  it("oauthTutorials_invalidGrantMarksReauthRequiredAndReauthorizeClearsIt (F-202 UC-003)", async () => {
    let currentNowMs = Date.parse("2026-10-22T12:00:00Z");

    const browserLauncher: IBrowserLauncher = {
      async openUrl(): Promise<Result<void>> {
        return ok(undefined);
      }
    };

    const oauthClient: IOAuthFlowClient = {
      async authorizeCode(request): Promise<
        Result<{
          readonly accessToken: string;
          readonly refreshToken: string;
          readonly expiresInSeconds: number;
          readonly returnedState?: string | undefined;
        }>
      > {
        return ok({
          accessToken: "fresh-access-token",
          refreshToken: "fresh-refresh-token",
          expiresInSeconds: 3600,
          returnedState: request.state
        });
      },
      async refreshToken(): Promise<
        Result<{
          readonly accessToken: string;
          readonly refreshToken?: string | undefined;
          readonly expiresInSeconds: number;
        }>
      > {
        return fail("invalid_grant: refresh token has been revoked", "Unauthorized");
      }
    };

    const clientRes = await ConfigurationClient.initialize({
      applicationName: "OAuthApp",
      configManifest: manifestPath,
      configDirectory: configDir,
      keyring,
      browserLauncher,
      oauthClient,
      clock: () => currentNowMs
    });
    expect(clientRes.ok).toBe(true);
    if (!clientRes.ok) return;
    const client = clientRes.value;

    const oauthOpts = {
      authorizationEndpoint: "https://github.example.com/login/oauth/authorize",
      tokenEndpoint: "https://github.example.com/login/oauth/access_token",
      clientId: "gh-client",
      scopes: ["repo"]
    };

    expect((await client.authorize("github", "oauthCred", oauthOpts)).ok).toBe(true);

    // Expire token and trigger invalid_grant
    currentNowMs = Date.parse("2026-10-22T14:00:00Z");
    const expiredRes = await client.get("github", "oauthCred");
    expect(expiredRes.ok).toBe(false);
    if (!expiredRes.ok) {
      expect(expiredRes.category).toBe("Unauthorized");
    }

    // Verify reauth_required is persisted on the entry in github.json
    const docOnDisk = JSON.parse(fs.readFileSync(path.join(configDir, "github.json"), "utf8")) as Record<
      string,
      { reauth_required?: boolean }
    >;
    expect(docOnDisk["oauthCred"]?.reauth_required).toBe(true);

    // Re-run authorize() to recover
    expect((await client.authorize("github", "oauthCred", oauthOpts)).ok).toBe(true);

    const docAfterReauth = JSON.parse(
      fs.readFileSync(path.join(configDir, "github.json"), "utf8")
    ) as Record<string, { reauth_required?: boolean }>;
    expect(docAfterReauth["oauthCred"]?.reauth_required).toBe(false);

    const recoveredToken = await client.get("github", "oauthCred");
    expect(recoveredToken.ok && recoveredToken.value).toBe("fresh-access-token");
  });

  it("oauthTutorials_pendingRotatedGrantJournal_replaysBeforeRefreshOnNextGet (F-202 UC-004)", async () => {
    let currentNowMs = Date.parse("2026-10-22T12:00:00Z");
    let refreshCalls = 0;

    const oauthClient: IOAuthFlowClient = {
      async authorizeCode(request) {
        return ok({
          accessToken: "initial-access",
          refreshToken: "initial-refresh",
          expiresInSeconds: 3600,
          returnedState: request.state
        });
      },
      async refreshToken() {
        refreshCalls++;
        return ok({
          accessToken: "rotated-access-from-journal",
          refreshToken: "rotated-refresh-from-journal",
          expiresInSeconds: 3600
        });
      }
    };

    const clientRes = await ConfigurationClient.initialize({
      applicationName: "OAuthApp",
      configManifest: manifestPath,
      configDirectory: configDir,
      keyring,
      oauthClient,
      clock: () => currentNowMs
    });
    expect(clientRes.ok).toBe(true);
    if (!clientRes.ok) return;
    const client = clientRes.value;

    expect(
      (
        await client.authorize("slack", "botGrant", {
          authorizationEndpoint: "https://slack.example.com/oauth/authorize",
          tokenEndpoint: "https://slack.example.com/oauth/token",
          clientId: "slack-client",
          scopes: ["chat:write"]
        })
      ).ok
    ).toBe(true);

    // Advance clock past expiry and simulate a disk write failure AFTER .pending/slack.oauth.json is written
    currentNowMs = Date.parse("2026-10-22T13:30:00Z");
    client.setDocumentWriteInterceptor(() => fail("Simulated disk full during OAuth refresh write", "Unavailable"));

    const failedGet = await client.get("slack", "botGrant");
    expect(failedGet.ok).toBe(false);
    expect(refreshCalls).toBe(1);

    const pendingOAuthPath = path.join(configDir, ".pending", "slack.oauth.json");
    expect(fs.existsSync(pendingOAuthPath)).toBe(true);

    // Remove disk failure interceptor; next get("slack", "botGrant") replays .pending/slack.oauth.json without calling refreshToken again!
    client.setDocumentWriteInterceptor(undefined);
    const recoveredGet = await client.get("slack", "botGrant");
    expect(recoveredGet.ok && recoveredGet.value).toBe("rotated-access-from-journal");
    expect(refreshCalls).toBe(1); // still 1 — did not call refreshToken a second time!
    expect(fs.existsSync(pendingOAuthPath)).toBe(false);
  });

  it("oauthTutorials_vaultCredentialResolutionAndOAuthRemoval_resolvesWithoutCycleAndRemovesCleanly (F-202 UC-005 & UC-006)", async () => {
    const currentNowMs = Date.parse("2026-10-22T12:00:00Z");
    const vaultSecrets = new Map<string, string>([["corp/apiSecret", "vault-secret-value-777"]]);
    let resolvedBearerToken = "";

    const oauthClient: IOAuthFlowClient = {
      async authorizeCode(request) {
        return ok({
          accessToken: "valid-oauth-vault-token",
          refreshToken: "valid-oauth-refresh-token",
          expiresInSeconds: 3600,
          returnedState: request.state
        });
      },
      async refreshToken() {
        return fail("Not needed");
      }
    };

    const fakeVaultFactory: IVaultProviderFactory = {
      kind: "azure-key-vault",
      create(_options, getCredential): Result<IVaultProvider> {
        return ok({
          name: "corpVault",
          async getSecret(name: string, sig?: AbortSignal): Promise<Result<string>> {
            const credRes = await getCredential(sig);
            if (!credRes.ok) return credRes;
            resolvedBearerToken = credRes.value;
            const val = vaultSecrets.get(name);
            return val ? ok(val) : fail("Not found", "NotFound");
          },
          async setSecret(name: string, val: string, sig?: AbortSignal): Promise<Result<void>> {
            const credRes = await getCredential(sig);
            if (!credRes.ok) return credRes;
            resolvedBearerToken = credRes.value;
            vaultSecrets.set(name, val);
            return ok(undefined);
          },
          async deleteSecret(
            name: string
          ): Promise<Result<{ readonly deleted: boolean; readonly retained: boolean }>> {
            vaultSecrets.delete(name);
            return ok({ deleted: true, retained: false });
          }
        });
      }
    };

    const clientRes = await ConfigurationClient.initialize({
      applicationName: "VaultOAuthApp",
      configManifest: manifestPath,
      configDirectory: configDir,
      keyring,
      oauthClient,
      keyVault: {
        name: "corpVault",
        kind: "azure-key-vault",
        credential: "azure-auth/vaultGrant",
        settings: { vaultUri: "https://corp.vault.azure.net/" }
      },
      vaultFactories: [fakeVaultFactory],
      clock: () => currentNowMs
    });
    expect(clientRes.ok).toBe(true);
    if (!clientRes.ok) return;
    const client = clientRes.value;

    // Authorize OAuth credential at "azure-auth/vaultGrant"
    expect(
      (
        await client.authorize("azure-auth", "vaultGrant", {
          authorizationEndpoint: "https://login.microsoftonline.com/common/oauth2/v2.0/authorize",
          tokenEndpoint: "https://login.microsoftonline.com/common/oauth2/v2.0/token",
          clientId: "azure-cli",
          scopes: ["https://vault.azure.net/.default"]
        })
      ).ok
    ).toBe(true);

    // Store a secret in corpVault -> resolves OAuth token from "azure-auth/vaultGrant"
    expect(
      (
        await client.set("corp", "apiSecret", "vault-secret-value-777", {
          encrypted: true,
          provider: "corpVault"
        })
      ).ok
    ).toBe(true);
    expect(resolvedBearerToken).toBe("valid-oauth-vault-token");

    // Removing OAuth credential via remove()
    const rmRes = await client.remove("azure-auth", "vaultGrant");
    expect(rmRes.ok).toBe(true);

    const afterRm = await client.get("azure-auth", "vaultGrant");
    expect(afterRm.ok).toBe(false);
    if (!afterRm.ok) {
      expect(afterRm.category).toBe("NotFound");
    }
  });
});
