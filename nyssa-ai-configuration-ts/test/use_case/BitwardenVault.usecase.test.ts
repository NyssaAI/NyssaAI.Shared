import { describe, it, expect, beforeEach, afterEach } from "vitest";
import * as fs from "node:fs";
import * as path from "node:path";
import * as os from "node:os";
import { ok, fail, type Result } from "@nyssa-ai/result";
import { ConfigurationClient } from "../../src/ConfigurationClient.js";
import { InMemoryKeyring } from "../../src/keyring/InMemoryKeyring.js";
import { VaultProviderBitwarden } from "../../src/vault/bitwarden/VaultProviderBitwarden.js";
import { VaultProviderFactoryBitwarden } from "../../src/vault/bitwarden/VaultProviderFactoryBitwarden.js";

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

  it("bitwardenVault_storesPointerLocallyAndResolvesSecretThroughBwsRunner", async () => {
    const remoteStore = new Map<string, { id: string; value: string }>();
    let nextId = 0;

    const factory = new VaultProviderFactoryBitwarden(
      VaultProviderFactoryBitwarden.DEFAULT_KIND,
      async (_binary, args, env): Promise<Result<string>> => {
        if (env[VaultProviderBitwarden.ACCESS_TOKEN_ENV] !== "bws-machine-token-xyz") {
          return fail("unauthorized", "Forbidden");
        }
        if (args[0] === "secret" && args[1] === "list") {
          const items = [...remoteStore.entries()].map(([key, v]) => ({ id: v.id, key }));
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

    const setRes = await client.set("payments", "webhookSecret", "whsec_live_bws_987", {
      provider: "bitwarden"
    });
    expect(setRes.ok).toBe(true);

    const rawDoc = JSON.parse(fs.readFileSync(path.join(configDir, "payments.json"), "utf8")) as Record<
      string,
      { value: unknown; provider?: string }
    >;
    expect(rawDoc["webhookSecret"]).toEqual({
      value: "payments/webhookSecret",
      provider: "bitwarden"
    });

    const getRes = await client.get("payments", "webhookSecret");
    expect(getRes.ok).toBe(true);
    if (getRes.ok) {
      expect(getRes.value).toBe("whsec_live_bws_987");
    }

    const remRes = await client.remove("payments", "webhookSecret");
    expect(remRes.ok).toBe(true);
    expect(remoteStore.size).toBe(0);
  });
});
