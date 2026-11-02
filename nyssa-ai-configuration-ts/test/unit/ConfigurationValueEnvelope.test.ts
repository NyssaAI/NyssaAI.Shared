import { describe, it, expect, beforeEach, afterEach } from "vitest";
import * as fs from "node:fs";
import * as path from "node:path";
import * as os from "node:os";
import * as crypto from "node:crypto";
import { ConfigurationEntryResolver } from "../../src/values/ConfigurationEntryResolver.js";
import { ConfigurationCipherAesGcm } from "../../src/encryption/ConfigurationCipherAesGcm.js";
import { OAuthTokenManager } from "../../src/oauth/OAuthTokenManager.js";
import { PendingJournalManager } from "../../src/recovery/PendingJournalManager.js";
import { ok } from "../../src/results/Result.js";
import type { IVaultProvider } from "../../src/vault/IVaultProvider.js";

describe("ConfigurationValueEnvelope", () => {
  let tempDir: string;

  beforeEach(() => {
    tempDir = fs.mkdtempSync(path.join(os.tmpdir(), "nyssa-value-envelope-"));
  });

  afterEach(() => {
    try {
      fs.rmSync(tempDir, { recursive: true, force: true });
    } catch {
      // ignore
    }
  });

  it("prepareEntryForProvider_cloudVaultPointer_omitsEncryptedWhileAesLocalEmitsEncryptedTrue", async () => {
    const cipherRes = ConfigurationCipherAesGcm.create(crypto.randomBytes(32));
    expect(cipherRes.ok).toBe(true);
    if (!cipherRes.ok) return;

    const fakeVault: IVaultProvider = {
      name: "azureKeyVault",
      async getSecret() {
        return ok("secret");
      },
      async setSecret() {
        return ok(undefined);
      },
      async deleteSecret() {
        return ok({ deleted: true, retained: false });
      }
    };

    const resolver = new ConfigurationEntryResolver(
      tempDir,
      10000,
      cipherRes.value,
      new Map([["azureKeyVault", fakeVault]]),
      new Map(),
      new OAuthTokenManager(tempDir, cipherRes.value),
      new PendingJournalManager(tempDir)
    );

    const vaultPrepared = await resolver.prepareEntryForProvider(
      "billing",
      "stripeSecretKey",
      "sk_live_123",
      "azureKeyVault"
    );
    expect(vaultPrepared.ok).toBe(true);
    if (vaultPrepared.ok) {
      expect(vaultPrepared.value.entry).toEqual({
        value: "billing/stripeSecretKey",
        provider: "azureKeyVault"
      });
      expect("encrypted" in vaultPrepared.value.entry).toBe(false);
    }

    const aesPrepared = await resolver.prepareEntryForProvider(
      "billing",
      "webhookSigningKey",
      "whsec_123",
      "aesLocal"
    );
    expect(aesPrepared.ok).toBe(true);
    if (aesPrepared.ok) {
      expect(aesPrepared.value.entry.encrypted).toBe(true);
      expect(aesPrepared.value.entry.provider).toBe("aesLocal");
    }
  });
});
