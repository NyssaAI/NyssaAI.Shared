// Responsibility: High-level store coordinating namespace document operations and cipher resolution.
// Public interface:
// +-- ConfigurationStore
// |   +-- get(namespace, key, signal?)
// |   +-- remove(namespace, key, signal?)
// |   `-- set(namespace, key, value, encrypted?, provider?, signal?)

import * as path from "node:path";
import { ok, fail, type Result } from "../results/Result.js";
import { ConfigurationDocumentLocalFile } from "./ConfigurationDocumentLocalFile.js";
import type { ConfigurationValue } from "./ConfigurationValue.js";
import type { IConfigurationCipher } from "../encryption/IConfigurationCipher.js";

/**
 * ConfigurationStore maps namespace names to document files and transparently seals/unseals values.
 */
export class ConfigurationStore {
  private readonly _configurationDirectory: string;
  private readonly _lockTimeoutMs: number;
  private readonly _cipher?: IConfigurationCipher;

  public constructor(
    configurationDirectory: string,
    lockTimeoutMs: number = 10000,
    cipher?: IConfigurationCipher
  ) {
    this._configurationDirectory = path.resolve(configurationDirectory);
    this._lockTimeoutMs = lockTimeoutMs;
    this._cipher = cipher;
  }

  private getDocument(namespace: string): ConfigurationDocumentLocalFile {
    const docPath = path.join(this._configurationDirectory, `${namespace}.json`);
    return new ConfigurationDocumentLocalFile(docPath, this._lockTimeoutMs);
  }

  public async get(namespace: string, key: string, signal?: AbortSignal): Promise<Result<unknown>> {
    const doc = this.getDocument(namespace);
    const readRes = await doc.read(signal);
    if (!readRes.ok) {
      return readRes;
    }

    const entry = readRes.value[key];
    if (!entry) {
      return fail(`Key '${key}' not found in namespace '${namespace}'.`);
    }

    if (entry.encrypted && entry.provider === "aesLocal") {
      if (!this._cipher) {
        return fail("Cannot decrypt entry: no cipher configured.");
      }
      if (typeof entry.value !== "string") {
        return fail("Encrypted entry must carry a string payload.");
      }
      const openRes = this._cipher.open(entry.value, namespace, key);
      if (!openRes.ok) {
        return openRes;
      }
      if (entry.json) {
        try {
          return ok(JSON.parse(openRes.value));
        } catch (err) {
          const message = err instanceof Error ? err.message : String(err);
          return fail(`Failed to deserialize JSON value for '${key}': ${message}`);
        }
      }
      return openRes;
    }

    // Literal value
    return ok(entry.value);
  }

  public async set(
    namespace: string,
    key: string,
    value: unknown,
    encrypted: boolean = false,
    provider?: string,
    signal?: AbortSignal
  ): Promise<Result<void>> {
    let finalValue: unknown = value;
    let isJson = false;

    if (encrypted) {
      const chosenProvider = provider || "aesLocal";
      if (chosenProvider === "aesLocal") {
        if (!this._cipher) {
          return fail("Cannot encrypt entry: no cipher configured.");
        }
        let serializedString: string;
        if (typeof value === "string") {
          serializedString = value;
        } else {
          serializedString = JSON.stringify(value);
          isJson = true;
        }

        const sealRes = this._cipher.seal(serializedString, namespace, key);
        if (!sealRes.ok) {
          return fail(sealRes.error);
        }
        finalValue = sealRes.value;
      }
    }

    const entry: ConfigurationValue = {
      value: finalValue,
      ...(encrypted ? { encrypted: true, provider: provider || "aesLocal" } : {}),
      ...(isJson ? { json: true } : {})
    };

    const doc = this.getDocument(namespace);
    return doc.modify((current) => {
      current[key] = entry;
      return ok(current);
    }, signal);
  }

  public async remove(namespace: string, key: string, signal?: AbortSignal): Promise<Result<boolean>> {
    const doc = this.getDocument(namespace);
    let removed = false;
    const modRes = await doc.modify((current) => {
      if (Object.prototype.hasOwnProperty.call(current, key)) {
        delete current[key];
        removed = true;
      }
      return ok(current);
    }, signal);

    if (!modRes.ok) {
      return modRes;
    }
    return ok(removed);
  }
}
