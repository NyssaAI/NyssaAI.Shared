import { describe, it, expect } from "vitest";
import { ok, fail, type Result } from "@nyssa-ai/result";
import { VaultProviderBitwarden } from "../../src/vault/bitwarden/VaultProviderBitwarden.js";
import { VaultProviderFactoryBitwarden } from "../../src/vault/bitwarden/VaultProviderFactoryBitwarden.js";
import { VaultProviderCaching } from "../../src/vault/VaultProviderCaching.js";
import { SecretCacheInMemory } from "../../src/vault/SecretCacheInMemory.js";
import type { IVaultProvider } from "../../src/vault/IVaultProvider.js";
import type { VaultOptions } from "../../src/manifest/VaultOptions.js";

describe("VaultProviderBitwarden & VaultProviderCaching Unit Tests", () => {
  const listWithoutValueJson = JSON.stringify([
    { id: "uuid-100", key: "db/password" },
    { id: "uuid-200", slug: "api.token" }
  ]);
  const listWithValueJson = JSON.stringify([
    { id: "uuid-100", key: "db/password", value: "inline-db-secret" },
    { id: "uuid-200", slug: "api.token", value: "inline-api-token" }
  ]);
  const getJson = JSON.stringify({
    id: "uuid-100",
    key: "db/password",
    value: "resolved-db-secret"
  });

  function createOptions(): VaultOptions {
    return {
      name: "bwsVault",
      kind: VaultProviderFactoryBitwarden.DEFAULT_KIND,
      endpoint: "https://api.bitwarden.com",
      settings: {
        projectId: "proj-123",
        organizationId: "org-456"
      },
      secrets: {
        accessToken: "machine-token-abc"
      }
    };
  }

  it("vaultProviderBitwarden_getSecret_whenListContainsValue_returnsInSingleCallWithoutSecretGet", async () => {
    const calls: Array<{ binary: string; args: readonly string[]; env: Readonly<Record<string, string>> }> = [];
    const provider = new VaultProviderBitwarden(createOptions(), undefined, async (binary, args, env) => {
      calls.push({ binary, args, env });
      return ok(listWithValueJson);
    });

    const res = await provider.getSecret("db/password");

    expect(res.ok).toBe(true);
    if (res.ok) {
      expect(res.value).toBe("inline-db-secret");
    }
    expect(calls).toHaveLength(1);
    expect(calls[0]?.args).toEqual(["secret", "list", "proj-123", "--output", "json"]);
  });

  it("vaultProviderBitwarden_getSecret_whenListOmitsValue_fallsBackToSecretGetByUuid", async () => {
    const calls: Array<{ binary: string; args: readonly string[]; env: Readonly<Record<string, string>> }> = [];
    const provider = new VaultProviderBitwarden(createOptions(), undefined, async (binary, args, env) => {
      calls.push({ binary, args, env });
      return ok(args[1] === "list" ? listWithoutValueJson : getJson);
    });

    const res = await provider.getSecret("db/password");

    expect(res.ok).toBe(true);
    if (res.ok) {
      expect(res.value).toBe("resolved-db-secret");
    }
    expect(calls).toHaveLength(2);
    expect(calls[0]?.args).toEqual(["secret", "list", "proj-123", "--output", "json"]);
    expect(calls[1]?.args).toEqual(["secret", "get", "uuid-100", "--output", "json"]);
    expect(calls[0]?.env[VaultProviderBitwarden.ACCESS_TOKEN_ENV]).toBe("machine-token-abc");
    expect(calls[0]?.env[VaultProviderBitwarden.ORGANIZATION_ID_ENV]).toBe("org-456");
  });

  it("vaultProviderBitwarden_getSecret_missingSecret_returnsNotFound", async () => {
    const provider = new VaultProviderBitwarden(createOptions(), undefined, async () => ok(listWithValueJson));

    const res = await provider.getSecret("missing/key");

    expect(res.ok).toBe(false);
    if (!res.ok) {
      expect(res.category).toBe("NotFound");
    }
  });

  it("vaultProviderBitwarden_setSecret_newAndExistingSecret_usesCreateThenEdit", async () => {
    const calls: Array<readonly string[]> = [];
    let hasItem = false;
    const provider = new VaultProviderBitwarden(createOptions(), undefined, async (_, args) => {
      calls.push(args);
      if (args[1] === "list") {
        return ok(hasItem ? listWithValueJson : "[]");
      }
      hasItem = true;
      return ok(getJson);
    });

    const createRes = await provider.setSecret("db/password", "initial-secret");
    const editRes = await provider.setSecret("db/password", "rotated-secret");

    expect(createRes.ok).toBe(true);
    expect(editRes.ok).toBe(true);
    expect(calls[1]).toEqual(["secret", "create", "db/password", "initial-secret", "proj-123", "--output", "json"]);
    expect(calls[3]).toEqual(["secret", "edit", "uuid-100", "--value", "rotated-secret", "--output", "json"]);
  });

  it("vaultProviderBitwarden_deleteSecret_existingAndMissing_deletesOrReturnsFalse", async () => {
    const calls: Array<readonly string[]> = [];
    const provider = new VaultProviderBitwarden(createOptions(), undefined, async (_, args) => {
      calls.push(args);
      return ok(args[1] === "list" ? listWithValueJson : "");
    });

    const delExisting = await provider.deleteSecret("db/password");
    const delMissing = await provider.deleteSecret("missing/key");

    expect(delExisting.ok).toBe(true);
    if (delExisting.ok) {
      expect(delExisting.value).toEqual({ deleted: true, retained: false });
    }
    expect(delMissing.ok).toBe(true);
    if (delMissing.ok) {
      expect(delMissing.value).toEqual({ deleted: false, retained: false });
    }
    expect(calls[1]).toEqual(["secret", "delete", "uuid-100"]);
  });

  it("vaultProviderFactoryBitwarden_missingCredentials_returnsValidationFailure", () => {
    const factory = new VaultProviderFactoryBitwarden();
    const res: Result<unknown> = factory.create(
      {
        name: "bwsVault",
        kind: VaultProviderFactoryBitwarden.DEFAULT_KIND
      },
      async () => fail("no cred", "NotFound")
    );

    expect(res.ok).toBe(false);
    if (!res.ok) {
      expect(res.category).toBe("Validation");
    }
  });

  it("vaultProviderCaching_default60sTtl_cachesAndInvalidatesOnWriteDeleteAndFailOpen", async () => {
    let nowMs = 10_000;
    let readCalls = 0;
    const store = new Map<string, string>([["payments/apiKey", "v1"]]);

    const inner: IVaultProvider = {
      name: "bitwarden",
      async getSecret(secretName: string) {
        readCalls++;
        const val = store.get(secretName);
        return val !== undefined ? ok(val) : fail("not found", "NotFound");
      },
      async setSecret(secretName: string, secretValue: string) {
        store.set(secretName, secretValue);
        return ok(undefined);
      },
      async deleteSecret(secretName: string) {
        store.delete(secretName);
        return ok({ deleted: true, retained: false });
      }
    };

    const sharedCache = new SecretCacheInMemory(() => nowMs);
    const wrappedDev = VaultProviderCaching.wrap(
      inner,
      { name: "bitwarden", kind: "bitwarden-secrets", settings: { projectId: "proj-dev" } },
      sharedCache,
      () => nowMs
    );
    expect(wrappedDev.ok).toBe(true);
    if (!wrappedDev.ok) return;
    const cachedDev = wrappedDev.value as VaultProviderCaching;
    expect(cachedDev.cacheTTLSeconds).toBe(60);

    expect((await cachedDev.getSecret("payments/apiKey")).ok).toBe(true);
    expect(readCalls).toBe(1);

    // Hit within 60s
    nowMs += 59_000;
    const hitRes = await cachedDev.getSecret("payments/apiKey");
    expect(hitRes.ok && hitRes.value).toBe("v1");
    expect(readCalls).toBe(1);

    // Scope isolation: prod vault with same sharedCache does not collide with dev vault
    store.set("payments/apiKey", "prod-v1");
    const wrappedProd = VaultProviderCaching.wrap(
      inner,
      { name: "bitwarden", kind: "bitwarden-secrets", settings: { projectId: "proj-prod" } },
      sharedCache,
      () => nowMs
    );
    expect(wrappedProd.ok).toBe(true);
    if (wrappedProd.ok) {
      const prodRes = await wrappedProd.value.getSecret("payments/apiKey");
      expect(prodRes.ok && prodRes.value).toBe("prod-v1");
      expect(readCalls).toBe(2);
    }

    // Expire after 60s
    store.set("payments/apiKey", "v2");
    nowMs += 2_000;
    const expiredRes = await cachedDev.getSecret("payments/apiKey");
    expect(expiredRes.ok && expiredRes.value).toBe("v2");
    expect(readCalls).toBe(3);

    // setSecret evicts immediately
    await cachedDev.setSecret("payments/apiKey", "v3");
    const afterSet = await cachedDev.getSecret("payments/apiKey");
    expect(afterSet.ok && afterSet.value).toBe("v3");
    expect(readCalls).toBe(4);

    // deleteSecret evicts immediately
    await cachedDev.deleteSecret("payments/apiKey");
    expect((await cachedDev.getSecret("payments/apiKey")).ok).toBe(false);
    expect(readCalls).toBe(5);

    // Fail-open when custom cache throws
    store.set("payments/apiKey", "resilient");
    const broken = new VaultProviderCaching(inner, 60, {
      get() {
        throw new Error("keyring unavailable");
      },
      set() {
        throw new Error("keyring quota exceeded");
      },
      remove() {
        throw new Error("keyring remove error");
      },
      clear() {
        throw new Error("keyring clear error");
      }
    });
    const brokenRead = await broken.getSecret("payments/apiKey");
    expect(brokenRead.ok && brokenRead.value).toBe("resilient");
  });
});
