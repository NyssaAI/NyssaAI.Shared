import { describe, it, expect } from "vitest";
import { ok, fail, type Result } from "@nyssa-ai/result";
import { VaultProviderBitwarden } from "../../src/vault/bitwarden/VaultProviderBitwarden.js";
import { VaultProviderFactoryBitwarden } from "../../src/vault/bitwarden/VaultProviderFactoryBitwarden.js";
import type { VaultOptions } from "../../src/manifest/VaultOptions.js";

describe("VaultProviderBitwarden Unit Tests", () => {
  const listJson = JSON.stringify([
    { id: "uuid-100", key: "db/password" },
    { id: "uuid-200", slug: "api.token" }
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

  it("vaultProviderBitwarden_getSecret_resolvesUuidAndReadsSecret", async () => {
    const calls: Array<{ binary: string; args: readonly string[]; env: Readonly<Record<string, string>> }> = [];
    const provider = new VaultProviderBitwarden(createOptions(), undefined, async (binary, args, env) => {
      calls.push({ binary, args, env });
      return ok(args[1] === "list" ? listJson : getJson);
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
    const provider = new VaultProviderBitwarden(createOptions(), undefined, async () => ok(listJson));

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
        return ok(hasItem ? listJson : "[]");
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
      return ok(args[1] === "list" ? listJson : "");
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
});
