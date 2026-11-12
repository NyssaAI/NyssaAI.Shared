// Responsibility: Read the 32-byte master encryption key from systemd encrypted credentials (CREDENTIALS_DIRECTORY).
// Public interface:
// `-- KeyringSystemdCreds
//     +-- IMMUTABLE_ERROR_MESSAGE
//     +-- MANIFEST_CREDENTIAL_FILENAMES
//     +-- delete(service, account, signal)
//     +-- findCredentialFile(credentialsDir, service, account, credentialName)
//     +-- get(service, account, signal)
//     +-- isAvailable()
//     +-- new(credentialName)
//     +-- sanitizeSegment(segment)
//     `-- set(service, account, secret, signal)

import * as fs from "node:fs";
import * as path from "node:path";
import { ok, fail, type Result } from "@nyssa-ai/result";
import { KeyringDefault } from "./KeyringDefault.js";
import type { IKeyring } from "./IKeyring.js";

/**
 * KeyringSystemdCreds reads the 256-bit (32-byte) master key from the in-memory RAMFS directory
 * provisioned by systemd (`process.env.CREDENTIALS_DIRECTORY`).
 */
export class KeyringSystemdCreds {
  public static readonly IMMUTABLE_ERROR_MESSAGE =
    "systemd-creds is immutable at runtime; credentials must be provisioned via systemd service configuration.";

  public static readonly MANIFEST_CREDENTIAL_FILENAMES = [
    "nyssa-ai-configuration._manifest",
    "_manifest.key",
    "_manifest",
    "nyssa-manifest-key",
    "nyssa-manifest-key.key"
  ] as const;

  private readonly _credentialName: string | undefined;

  public constructor(credentialName?: string) {
    const trimmed = credentialName?.trim();
    this._credentialName = trimmed && trimmed.length > 0 ? trimmed : undefined;
  }

  public static sanitizeSegment(segment: string): string {
    return segment.replace(/[^A-Za-z0-9._-]/g, "_");
  }

  /**
   * Returns true if CREDENTIALS_DIRECTORY is set and points to an existing directory.
   */
  public static isAvailable(): boolean {
    const dir = process.env["CREDENTIALS_DIRECTORY"];
    if (!dir || dir.trim().length === 0) {
      return false;
    }
    try {
      return fs.statSync(dir.trim()).isDirectory();
    } catch {
      return false;
    }
  }

  /**
   * Resolves a credential file path inside CREDENTIALS_DIRECTORY with strict isolation
   * between the shared '_manifest' master key and per-application keys.
   */
  public static findCredentialFile(
    credentialsDir: string,
    service: string,
    account: string,
    credentialName?: string
  ): Result<string | null> {
    const isManifest = account === KeyringDefault.MANIFEST_ACCOUNT;
    const sanitizedService = this.sanitizeSegment(service);
    const sanitizedName = `${sanitizedService}.${this.sanitizeSegment(account)}`;
    const candidates: readonly string[] = isManifest
      ? [`${sanitizedService}._manifest`, ...this.MANIFEST_CREDENTIAL_FILENAMES]
      : credentialName !== undefined
        ? [credentialName, `${credentialName}.key`]
        : [
            sanitizedName,
            `${sanitizedName}.key`,
            `${service}.key`,
            `${account}.key`,
            service,
            account,
            "nyssa-key",
            "nyssa-key.key",
            "master-key",
            "master-key.key"
          ];

    for (const name of candidates) {
      if (name.includes("/") || name.includes("\\") || name === ".." || name === ".") {
        continue;
      }
      const candidatePath = path.join(credentialsDir, name);
      if (path.dirname(candidatePath) === credentialsDir && fs.existsSync(candidatePath)) {
        try {
          if (fs.statSync(candidatePath).isFile()) {
            return ok(candidatePath);
          }
        } catch {
          // continue checking candidates
        }
      }
    }

    if (!isManifest && credentialName === undefined) {
      const manifestSet = new Set<string>(this.MANIFEST_CREDENTIAL_FILENAMES);
      try {
        const entries = fs.readdirSync(credentialsDir, { withFileTypes: true });
        const files = entries.filter(
          (e) => e.isFile() && !manifestSet.has(e.name) && !e.name.endsWith("._manifest")
        );
        if (files.length === 1 && files[0] !== undefined) {
          return ok(path.join(credentialsDir, files[0].name));
        }
      } catch (err) {
        const message = err instanceof Error ? err.message : String(err);
        return fail(`systemd-creds: failed to list '${credentialsDir}': ${message}`, "Unavailable");
      }
    }

    return ok(null);
  }

  /**
   * Returns true if CREDENTIALS_DIRECTORY is set and points to an existing directory.
   */
  public isAvailable(): boolean {
    return KeyringSystemdCreds.isAvailable();
  }

  /**
   * Reads and validates the 32-byte master key from CREDENTIALS_DIRECTORY.
   */
  public async get(service: string, account: string, signal?: AbortSignal): Promise<Result<Buffer>> {
    signal?.throwIfAborted();

    if (!service || service.trim().length === 0 || !account || account.trim().length === 0) {
      return fail("keyring: service and account must be non-empty.", "Validation");
    }
    if (/[\x00-\x1F\x7F]/.test(service) || /[\x00-\x1F\x7F]/.test(account)) {
      return fail("keyring: service and account must not contain control characters.", "Validation");
    }

    const rawDir = process.env["CREDENTIALS_DIRECTORY"];
    if (!rawDir || rawDir.trim().length === 0) {
      return fail("systemd-creds: CREDENTIALS_DIRECTORY is not set.", "NotFound");
    }

    const credentialsDir = path.resolve(rawDir.trim());
    if (!KeyringSystemdCreds.isAvailable()) {
      return fail(`systemd-creds: CREDENTIALS_DIRECTORY '${credentialsDir}' does not exist.`, "NotFound");
    }

    const trimmedService = service.trim();
    const trimmedAccount = account.trim();
    const fileRes = KeyringSystemdCreds.findCredentialFile(
      credentialsDir,
      trimmedService,
      trimmedAccount,
      this._credentialName
    );
    if (!fileRes.ok) {
      return fileRes;
    }
    if (fileRes.value === null) {
      return fail(
        `systemd-creds: no credential file found for '${trimmedService}/${trimmedAccount}' in '${credentialsDir}'.`,
        "NotFound"
      );
    }

    try {
      const rawBytes = fs.readFileSync(fileRes.value);
      return this.parseKeyMaterial(rawBytes, fileRes.value);
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err);
      return fail(`systemd-creds: failed to read '${fileRes.value}': ${message}`, "Unavailable");
    }
  }

  /**
   * Rejects runtime writes because systemd credentials are immutable at runtime.
   */
  public async set(
    service: string,
    account: string,
    secret: Buffer,
    signal?: AbortSignal
  ): Promise<Result<void>> {
    void service;
    void account;
    void secret;
    signal?.throwIfAborted();
    return fail(KeyringSystemdCreds.IMMUTABLE_ERROR_MESSAGE, "Forbidden");
  }

  /**
   * Rejects runtime deletions because systemd credentials are immutable at runtime.
   */
  public async delete(service: string, account: string, signal?: AbortSignal): Promise<Result<void>> {
    void service;
    void account;
    signal?.throwIfAborted();
    return fail(KeyringSystemdCreds.IMMUTABLE_ERROR_MESSAGE, "Forbidden");
  }

  private parseKeyMaterial(rawBytes: Buffer, filePath: string): Result<Buffer> {
    if (rawBytes.length === 0) {
      return fail(`systemd-creds: credential file '${filePath}' is empty.`, "Validation");
    }

    if (rawBytes.length === 32) {
      return ok(Buffer.from(rawBytes));
    }

    const trimmed = rawBytes.toString("utf8").trim();
    if (trimmed.length === 0) {
      return fail(`systemd-creds: credential file '${filePath}' is empty.`, "Validation");
    }

    if (trimmed.length % 4 !== 0 || !/^[A-Za-z0-9+/]+={0,2}$/.test(trimmed)) {
      return fail(
        `systemd-creds: credential file '${filePath}' contains malformed base64 or invalid key length.`,
        "Validation"
      );
    }

    const decoded = Buffer.from(trimmed, "base64");
    if (decoded.length !== 32) {
      return fail(
        `systemd-creds: credential file '${filePath}' decoded to ${decoded.length} bytes; expected 32 bytes.`,
        "Validation"
      );
    }

    return ok(decoded);
  }
}

const _typeCheck: IKeyring = null as unknown as KeyringSystemdCreds;
void _typeCheck;
