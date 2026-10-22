import { describe, it, expect } from "vitest";
import * as crypto from "node:crypto";
import { ConfigurationCipherAesGcm } from "../../src/encryption/ConfigurationCipherAesGcm.js";

describe("ConfigurationCipherAesGcm", () => {
  const testKey = crypto.randomBytes(32);

  it("cipherAesGcm_createWithInvalidKeyLength_returnsFailure", () => {
    const invalidKey = crypto.randomBytes(16);
    const res = ConfigurationCipherAesGcm.create(invalidKey);
    expect(res.ok).toBe(false);
  });

  it("cipherAesGcm_validPayload_roundtripsCorrectly", () => {
    const cipherRes = ConfigurationCipherAesGcm.create(testKey);
    expect(cipherRes.ok).toBe(true);
    if (!cipherRes.ok) return;

    const cipher = cipherRes.value;
    const plaintext = "super-secret-password-123!";
    const namespace = "database";
    const key = "connectionPassword";

    const sealRes = cipher.seal(plaintext, namespace, key);
    expect(sealRes.ok).toBe(true);
    if (!sealRes.ok) return;

    const openRes = cipher.open(sealRes.value, namespace, key);
    expect(openRes.ok).toBe(true);
    if (!openRes.ok) return;

    expect(openRes.value).toBe(plaintext);
  });

  it("cipherAesGcm_differentNamespace_failsAuthentication", () => {
    const cipher = ConfigurationCipherAesGcm.create(testKey);
    if (!cipher.ok) throw new Error("cipher creation failed");

    const sealRes = cipher.value.seal("secret", "appNamespace", "secretKey");
    expect(sealRes.ok).toBe(true);
    if (!sealRes.ok) return;

    // Open with different namespace
    const openRes = cipher.value.open(sealRes.value, "wrongNamespace", "secretKey");
    expect(openRes.ok).toBe(false);
  });

  it("cipherAesGcm_tamperedCiphertext_failsAuthentication", () => {
    const cipher = ConfigurationCipherAesGcm.create(testKey);
    if (!cipher.ok) throw new Error("cipher creation failed");

    const sealRes = cipher.value.seal("secret", "ns", "k");
    expect(sealRes.ok).toBe(true);
    if (!sealRes.ok) return;

    // Tamper with the base64 string
    const tampered = sealRes.value.slice(0, -2) + "ab";
    const openRes = cipher.value.open(tampered, "ns", "k");
    expect(openRes.ok).toBe(false);
  });
});
