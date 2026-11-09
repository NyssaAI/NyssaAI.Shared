import { describe, it, expect, beforeEach, afterEach } from "vitest";
import * as fs from "node:fs";
import * as path from "node:path";
import * as os from "node:os";
import { ok, fail, type Result } from "@nyssa-ai/result";
import { ConfigurationClient } from "../../src/ConfigurationClient.js";
import { InMemoryKeyring } from "../../src/keyring/InMemoryKeyring.js";
import { VaultProviderBitwarden } from "../../src/vault/bitwarden/VaultProviderBitwarden.js";
import { VaultProviderFactoryBitwarden } from "../../src/vault/bitwarden/VaultProviderFactoryBitwarden.js";
import type { SecretCache } from "../../src/vault/SecretCache.js";

describe("Bitwarden Secrets Manager (bws) Use Cases", () => {
  let tempDir: string;
  let manifestPath: string;
  let configDir: string;

  beforeEach(() => {
    tempDir = fs.mkdtempSync(path.join(os.tmpdir(), "nyssa-bws-vault-"));
    manifestPath = path.join(tempDir, "config-manifest.json");
    configDir = path.join(tempDir, "config");
  });

  afterEach(() => {
    try {
      fs.rmSync(tempDir, { recursive: true, force: true });
    } catch {
      // ignore
    }
  });

  it("bitwardenVault_storesPointerLocallyAndResolvesSecretInSingleListCallWithPreWarming", async () => {
    const remoteStore = new Map<string, { id: string; value: string }>();
    let nextId = 0;
    let listCalls = 0;
    let getCalls = 0;

    const factory = new VaultProviderFactoryBitwarden(
      VaultProviderFactoryBitwarden.DEFAULT_KIND,
      async (_binary, args, env): Promise<Result<string>> => {
        if (env[VaultProviderBitwarden.ACCESS_TOKEN_ENV] !== "bws-machine-token-xyz") {
          return fail("unauthorized", "Forbidden");
        }
        if (args[0] === "secret" && args[1] === "list") {
          listCalls++;
          const items = [...remoteStore.entries()].map(([key, v]) => ({ id: v.id, key, value: v.value }));
          return ok(JSON.stringify(items));
        }
        if (args[0] === "secret" && args[1] === "create") {
          const key = args[2]!;
          const value = args[3]!;
          const id = `bws-uuid-${++nextId}`;
          remoteStore.set(key, { id, value });
          return ok(JSON.stringify({ id, key, value }));
        }
        if (args[0] === "secret" && args[1] === "edit") {
          const editId = args[2]!;
          const newVal = args[4]!;
          for (const [k, v] of remoteStore.entries()) {
            if (v.id === editId) {
              remoteStore.set(k, { id: editId, value: newVal });
              return ok(JSON.stringify({ id: editId, key: k, value: newVal }));
            }
          }
          return fail("404 Not Found", "NotFound");
        }
        if (args[0] === "secret" && args[1] === "get") {
          getCalls++;
          const getId = args[2]!;
          for (const [k, v] of remoteStore.entries()) {
            if (v.id === getId) {
              return ok(JSON.stringify({ id: getId, key: k, value: v.value }));
            }
          }
          return fail("404 Not Found", "NotFound");
        }
        if (args[0] === "secret" && args[1] === "delete") {
          const delId = args[2]!;
          for (const [k, v] of remoteStore.entries()) {
            if (v.id === delId) {
              remoteStore.delete(k);
            }
          }
          return ok("");
        }
        return fail("unexpected command", "Unavailable");
      }
    );

    const clientRes = await ConfigurationClient.initialize({
      applicationName: "BwsPaymentsApp",
      configManifest: manifestPath,
      configDirectory: configDir,
      keyring: new InMemoryKeyring(),
      keyVault: {
        name: "bitwarden",
        kind: "bitwarden-secrets",
        endpoint: "https://api.bitwarden.com",
        settings: {
          organizationId: "11111111-2222-3333-4444-555555555555",
          projectId: "99999999-8888-7777-6666-555555555555"
        },
        secrets: {
          accessToken: "bws-machine-token-xyz"
        }
      },
      vaultFactories: [factory]
    });

    expect(clientRes.ok).toBe(true);
    if (!clientRes.ok) return;
    const client = clientRes.value;

    expect((await client.set("payments", "webhookSecret", "whsec_live_bws_987", { provider: "bitwarden" })).ok).toBe(true);
    expect((await client.set("payments", "apiKey", "sk_live_bws_123", { provider: "bitwarden" })).ok).toBe(true);

    const rawDoc = JSON.parse(fs.readFileSync(path.join(configDir, "payments.json"), "utf8")) as Record<
      string,
      { value: unknown; provider?: string }
    >;
    expect(rawDoc["webhookSecret"]).toEqual({
      value: "payments/webhookSecret",
      provider: "bitwarden"
    });

    const listsBeforeAssemble = listCalls;
    const nsRes = await client.getNamespace<{ webhookSecret: string; apiKey: string }>("payments");
    expect(nsRes.ok).toBe(true);
    if (nsRes.ok) {
      expect(nsRes.value).toEqual({
        webhookSecret: "whsec_live_bws_987",
        apiKey: "sk_live_bws_123"
      });
    }
    expect(listCalls).toBe(listsBeforeAssemble + 1);
    expect(getCalls).toBe(0);

    expect((await client.remove("payments", "webhookSecret")).ok).toBe(true);
    expect((await client.remove("payments", "apiKey")).ok).toBe(true);
    expect(remoteStore.size).toBe(0);
  });

  it("bitwardenVault_customSecretCacheAndCacheTTLSeconds_sharesCacheAcrossShortLivedClientInstances", async () => {
    const remoteStore = new Map<string, { id: string; value: string }>([
      ["payments/apiKey", { id: "id-1", value: "v1-secret" }]
    ]);
    let listCalls = 0;
    let nowMs = 50_000;

    const factory = new VaultProviderFactoryBitwarden(
      VaultProviderFactoryBitwarden.DEFAULT_KIND,
      async (_binary, args): Promise<Result<string>> => {
        if (args[0] === "secret" && args[1] === "list") {
          listCalls++;
          const items = [...remoteStore.entries()].map(([key, v]) => ({ id: v.id, key, value: v.value }));
          return ok(JSON.stringify(items));
        }
        if (args[0] === "secret" && args[1] === "edit") {
          const editId = args[2]!;
          const newVal = args[4]!;
          remoteStore.set("payments/apiKey", { id: editId, value: newVal });
          return ok(JSON.stringify({ id: editId, key: "payments/apiKey", value: newVal }));
        }
        return fail("unexpected command", "Unavailable");
      }
    );

    const entries = new Map<string, { value: string; expiresAtMs: number }>();
    const customCache: SecretCache = {
      get(key: string) {
        const entry = entries.get(key);
        if (!entry || nowMs >= entry.expiresAtMs) {
          entries.delete(key);
          return undefined;
        }
        return entry.value;
      },
      set(key: string, value: string, ttlSeconds: number) {
        entries.set(key, { value, expiresAtMs: nowMs + ttlSeconds * 1000 });
      },
      remove(key: string) {
        entries.delete(key);
      },
      clear() {
        entries.clear();
      }
    };

    const keyring = new InMemoryKeyring();
    async function makeClient(): Promise<ConfigurationClient> {
      const res = await ConfigurationClient.initialize({
        applicationName: "CliApp",
        configManifest: manifestPath,
        configDirectory: configDir,
        keyring,
        clock: () => nowMs,
        secretCache: customCache,
        keyVault: {
          name: "bitwarden",
          kind: "bitwarden-secrets",
          cacheTTLSeconds: 60,
          settings: { projectId: "proj-cli" },
          secrets: { accessToken: "bws-token" }
        },
        vaultFactories: [factory]
      });
      expect(res.ok).toBe(true);
      if (!res.ok) throw new Error(res.message);
      return res.value;
    }

    const cli1 = await makeClient();
    await cli1.set("payments", "apiKey", "v1-secret", { provider: "bitwarden" });
    const listsAfterSet = listCalls;
    const firstGet = await cli1.get("payments", "apiKey");
    expect(firstGet.ok && firstGet.value).toBe("v1-secret");
    expect(listCalls).toBe(listsAfterSet + 1);

    // Second short-lived CLI client 15s later hits customCache without invoking bws
    nowMs += 15_000;
    const cli2 = await makeClient();
    const secondGet = await cli2.get("payments", "apiKey");
    expect(secondGet.ok && secondGet.value).toBe("v1-secret");
    expect(listCalls).toBe(listsAfterSet + 1);

    // Third CLI client after 60s TTL expires calls bws again
    remoteStore.set("payments/apiKey", { id: "id-1", value: "v2-secret" });
    nowMs += 50_000;
    const cli3 = await makeClient();
    const thirdGet = await cli3.get("payments", "apiKey");
    expect(thirdGet.ok && thirdGet.value).toBe("v2-secret");
    expect(listCalls).toBe(listsAfterSet + 2);
  });
});
