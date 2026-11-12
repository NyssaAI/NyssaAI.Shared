import { describe, it, expect, vi } from "vitest";
import * as fs from "node:fs";
import * as path from "node:path";
import * as os from "node:os";
import * as http from "node:http";
import { Readable, Writable } from "node:stream";
import { ok, fail, type Result } from "@nyssa-ai/result";
import { ConfigurationClient } from "../../src/ConfigurationClient.js";
import { CliSecretInputProvider } from "../../src/secretInput/CliSecretInputProvider.js";
import { LoopbackSecretInputProvider } from "../../src/secretInput/LoopbackSecretInputProvider.js";
import { CapturedSecret } from "../../src/secretInput/CapturedSecret.js";
import type { ISecretInputProvider } from "../../src/secretInput/ISecretInputProvider.js";
import type { SecretPromptRequest } from "../../src/secretInput/SecretPromptRequest.js";
import type { IBrowserLauncher } from "../../src/oauth/IBrowserLauncher.js";
import type { IOAuthFlowClient } from "../../src/oauth/IOAuthFlowClient.js";

class MockSecretInputProvider implements ISecretInputProvider {
  constructor(private readonly captured: CapturedSecret) {}

  async captureSecret(_request: SecretPromptRequest): Promise<Result<CapturedSecret>> {
    return ok(this.captured);
  }
}

class FailingBrowserLauncher implements IBrowserLauncher {
  async openUrl(_url: string): Promise<Result<void>> {
    return fail("Browser failed to launch", "Unavailable");
  }
}

describe("Secret Capture and OAuth CLI", () => {
  it("cliSecretInputProvider_matchingConfirmation_capturesSecret", async () => {
    let capturedOutput = "";
    const streamOut = new Writable({
      write(chunk, _encoding, callback) {
        capturedOutput += chunk.toString();
        callback();
      }
    });

    // Provide 2 for provider choice, then password, then confirmation
    const inputContent = "2\nmySuperSecret123!\nmySuperSecret123!\n";
    const streamIn = Readable.from(inputContent.split(""));

    const provider = new CliSecretInputProvider(streamIn as NodeJS.ReadableStream, streamOut as NodeJS.WritableStream);
    const request: SecretPromptRequest = {
      namespace: "database",
      key: "password",
      promptMessage: "Enter DB password",
      availableProviders: ["aesLocal", "bws"],
      defaultProvider: "aesLocal",
      confirm: true
    };

    const res = await provider.captureSecret(request);
    expect(res.ok).toBe(true);
    if (!res.ok) return;

    expect(res.value.secret).toBe("mySuperSecret123!");
    expect(res.value.selectedProvider).toBe("bws");
    expect(capturedOutput).toContain("Encrypted Locally (AES-256-GCM)");
    expect(capturedOutput).toContain("Key Vault (Bitwarden Secrets Manager) [bws]");
    res.value.dispose();
  });

  it("cliAndLoopbackSecretInputProviders_multiBitwardenVaults_distinguishProviderLabelsWithBrackets", async () => {
    let cliOut = "";
    const streamOut = new Writable({
      write(chunk, _encoding, callback) {
        cliOut += chunk.toString();
        callback();
      }
    });
    const streamIn = Readable.from("4\nprodSecret!\n".split(""));
    const cliProvider = new CliSecretInputProvider(
      streamIn as NodeJS.ReadableStream,
      streamOut as NodeJS.WritableStream
    );

    const request: SecretPromptRequest = {
      namespace: "payments",
      key: "apiKey",
      promptMessage: "Enter API key",
      availableProviders: ["aesLocal", "literal", "bws-corporate", "bws-prod", "bws-staging"],
      defaultProvider: "aesLocal",
      confirm: false,
      timeoutMs: 5000
    };

    const cliRes = await cliProvider.captureSecret(request);
    expect(cliRes.ok).toBe(true);
    if (cliRes.ok) {
      expect(cliRes.value.selectedProvider).toBe("bws-prod");
      cliRes.value.dispose();
    }
    expect(cliOut).toContain("[1] Encrypted Locally (AES-256-GCM) (Default)");
    expect(cliOut).not.toContain("[aesLocal]");
    expect(cliOut).toContain("[2] Free Text (Unencrypted Plaintext)");
    expect(cliOut).not.toContain("[literal]");
    expect(cliOut).toContain("[3] Key Vault (Bitwarden Secrets Manager) [bws-corporate]");
    expect(cliOut).toContain("[4] Key Vault (Bitwarden Secrets Manager) [bws-prod]");
    expect(cliOut).toContain("[5] Key Vault (Bitwarden Secrets Manager) [bws-staging]");

    // Verify Loopback GET HTML renders distinct bracketed labels in <option> elements
    const silentOut = new Writable({
      write(_chunk, _enc, cb) {
        cb();
      }
    });
    const loopback = new LoopbackSecretInputProvider(undefined, silentOut as NodeJS.WritableStream);
    const capturePromise = loopback.captureSecret(request);
    const url = await loopback.waitForUrl(5000);
    expect(url).toBeDefined();

    const htmlBody = await new Promise<string>((resolve, reject) => {
      http
        .get(url!, (res) => {
          let data = "";
          res.on("data", (chunk) => {
            data += chunk.toString();
          });
          res.on("end", () => resolve(data));
        })
        .on("error", reject);
    });

    expect(htmlBody).toContain(">Encrypted Locally (AES-256-GCM)</option>");
    expect(htmlBody).toContain(">Free Text (Unencrypted Plaintext)</option>");
    expect(htmlBody).toContain(">Key Vault (Bitwarden Secrets Manager) [bws-corporate]</option>");
    expect(htmlBody).toContain(">Key Vault (Bitwarden Secrets Manager) [bws-prod]</option>");
    expect(htmlBody).toContain(">Key Vault (Bitwarden Secrets Manager) [bws-staging]</option>");

    loopback.cancel();
    await capturePromise;
  });

  it("cliSecretInputProvider_mismatchedConfirmation_failsValidation", async () => {
    const streamOut = new Writable({
      write(_chunk, _encoding, callback) {
        callback();
      }
    });

    const inputContent = "mySecret1\nmySecret2\n";
    const streamIn = Readable.from(inputContent.split(""));

    const provider = new CliSecretInputProvider(streamIn as NodeJS.ReadableStream, streamOut as NodeJS.WritableStream);
    const request: SecretPromptRequest = {
      namespace: "database",
      key: "password",
      promptMessage: "Enter DB password",
      availableProviders: ["aesLocal"],
      defaultProvider: "aesLocal",
      confirm: true
    };

    const res = await provider.captureSecret(request);
    expect(res.ok).toBe(false);
    if (res.ok) return;
    expect(res.category).toBe("Validation");
    expect(res.message.toLowerCase()).toContain("match");
  });

  it("loopbackSecretInputProvider_validPost_capturesSecret", async () => {
    const silentOut = new Writable({
      write(_chunk, _enc, cb) {
        cb();
      }
    });
    const provider = new LoopbackSecretInputProvider(undefined, silentOut as NodeJS.WritableStream);
    const request: SecretPromptRequest = {
      namespace: "vault",
      key: "token",
      promptMessage: "Enter token",
      availableProviders: ["aesLocal"],
      defaultProvider: "aesLocal",
      confirm: true,
      timeoutMs: 5000
    };

    const capturePromise = provider.captureSecret(request);
    const url = await provider.waitForUrl(5000);
    expect(url).toBeDefined();

    const postData = new URLSearchParams({
      session_token: provider.sessionToken,
      secret: "capturedLoopbackSecret",
      confirm_secret: "capturedLoopbackSecret",
      provider: "aesLocal"
    }).toString();

    const parsed = new URL(url!);
    await new Promise<void>((resolve, reject) => {
      const req = http.request(
        {
          hostname: parsed.hostname,
          port: parsed.port,
          path: parsed.pathname + parsed.search,
          method: "POST",
          headers: {
            "Content-Type": "application/x-www-form-urlencoded",
            "Content-Length": Buffer.byteLength(postData)
          }
        },
        (res) => {
          expect(res.statusCode).toBe(200);
          res.resume();
          res.on("end", () => resolve());
        }
      );
      req.on("error", reject);
      req.write(postData);
      req.end();
    });

    const res = await capturePromise;
    expect(res.ok).toBe(true);
    if (!res.ok) return;
    expect(res.value.secret).toBe("capturedLoopbackSecret");
    expect(res.value.selectedProvider).toBe("aesLocal");
    res.value.dispose();
  });

  it("loopbackSecretInputProvider_invalidToken_returns403", async () => {
    const silentOut = new Writable({
      write(_chunk, _enc, cb) {
        cb();
      }
    });
    const provider = new LoopbackSecretInputProvider(undefined, silentOut as NodeJS.WritableStream);
    const request: SecretPromptRequest = {
      namespace: "vault",
      key: "token",
      promptMessage: "Enter token",
      availableProviders: ["aesLocal"],
      defaultProvider: "aesLocal",
      confirm: false,
      timeoutMs: 3000
    };

    const capturePromise = provider.captureSecret(request);
    const url = await provider.waitForUrl(3000);
    expect(url).toBeDefined();

    const postData = new URLSearchParams({
      session_token: "invalid-token",
      secret: "secret",
      provider: "aesLocal"
    }).toString();

    const parsed = new URL(url!);
    await new Promise<void>((resolve, reject) => {
      const req = http.request(
        {
          hostname: parsed.hostname,
          port: parsed.port,
          path: parsed.pathname,
          method: "POST",
          headers: {
            "Content-Type": "application/x-www-form-urlencoded",
            "Content-Length": Buffer.byteLength(postData)
          }
        },
        (res) => {
          expect(res.statusCode).toBe(403);
          res.resume();
          res.on("end", () => resolve());
        }
      );
      req.on("error", reject);
      req.write(postData);
      req.end();
    });

    provider.cancel();
    await capturePromise;
  });

  it("configurationClient_captureSecret_encryptsAndReturnsReceipt", async () => {
    const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), "nyssa-secret-capture-"));
    const configDir = path.join(tempDir, "config");
    const manifestPath = path.join(tempDir, "manifest.json");

    try {
      const clientRes = await ConfigurationClient.initialize({
        applicationName: "SecretTestApp",
        configManifest: manifestPath,
        configDirectory: configDir
      });
      expect(clientRes.ok).toBe(true);
      if (!clientRes.ok) return;

      const client = clientRes.value;
      const mockInput = new MockSecretInputProvider(new CapturedSecret("plaintextPassword123!", "aesLocal"));

      const captureRes = await client.captureSecret("database", "password", {
        promptMessage: "Enter DB Password",
        inputProvider: mockInput
      });

      expect(captureRes.ok).toBe(true);
      if (!captureRes.ok) return;

      const receipt = captureRes.value;
      expect(receipt.namespace).toBe("database");
      expect(receipt.key).toBe("password");
      expect(receipt.provider).toBe("aesLocal");
      expect(receipt.storedAtUtc).toBeInstanceOf(Date);

      const docPath = path.join(configDir, "database.json");
      const content = fs.readFileSync(docPath, "utf-8");
      expect(content).not.toContain("plaintextPassword123!");
      expect(content).toContain('"provider": "aesLocal"');
      expect(content).toContain('"encrypted": true');

      const getRes = await client.get("database", "password");
      expect(getRes.ok).toBe(true);
      if (getRes.ok) {
        expect(getRes.value).toBe("plaintextPassword123!");
      }
    } finally {
      fs.rmSync(tempDir, { recursive: true, force: true });
    }
  });

  it("oauthFlow_continuesAndAuthenticatesWhenBrowserLauncherFails", async () => {
    const failingLauncher = new FailingBrowserLauncher();
    const stdoutWrite = vi.spyOn(process.stdout, "write").mockImplementation(() => true);

    const fakeFlowClient: IOAuthFlowClient = {
      async authorizeCode(req) {
        return ok({
          accessToken: "test-access-token",
          refreshToken: "test-refresh-token",
          expiresInSeconds: 3600,
          returnedState: req.state
        });
      },
      async refreshToken() {
        return ok({
          accessToken: "new-access",
          expiresInSeconds: 3600
        });
      }
    };

    const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), "nyssa-oauth-cli-"));
    const configDir = path.join(tempDir, "config");
    const manifestPath = path.join(tempDir, "manifest.json");

    try {
      const clientRes = await ConfigurationClient.initialize({
        applicationName: "OAuthCliApp",
        configManifest: manifestPath,
        configDirectory: configDir,
        browserLauncher: failingLauncher,
        oauthClient: fakeFlowClient
      });
      expect(clientRes.ok).toBe(true);
      if (!clientRes.ok) return;

      const client = clientRes.value;
      const authRes = await client.authorize("oauthTest", "grant", {
        authorizationEndpoint: "https://auth.example.test/authorize",
        tokenEndpoint: "https://auth.example.test/token",
        clientId: "my-test-client"
      });

      expect(authRes.ok).toBe(true);

      const printed = stdoutWrite.mock.calls.map((c) => c[0].toString()).join("");
      expect(printed).toContain("https://auth.example.test/authorize");
      expect(printed).toContain("client_id=my-test-client");
    } finally {
      stdoutWrite.mockRestore();
      fs.rmSync(tempDir, { recursive: true, force: true });
    }
  });
});
