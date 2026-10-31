// Responsibility: Entry sealing, unsealing, provider resolution, circular credential checks, and journal replay for ConfigurationStore.
// Public interface:
// `-- ConfigurationEntryResolver
//     +-- canonicalProvider(provider)
//     +-- checkCircularCredential(providerName, namespace, key)
//     +-- getDocument(namespace)
//     +-- getDocumentPath(namespace)
//     +-- invokeWriteInterceptor(namespace)
//     +-- journalManager
//     +-- new(configurationDirectory, lockTimeoutMs, cipher, vaultProviders, vaultCredentials, oauthManager, journalManager)
//     +-- normalizeProviderName(entry)
//     +-- oauthManager
//     +-- prepareEntryForProvider(namespace, key, value, providerName, signal)
//     +-- replayPendingJournalsIfPresent(namespace, signal, expectedOperationId)
//     +-- resolveEntryValue(namespace, key, entry, signal)
//     +-- setDocumentWriteInterceptor(interceptor)
//     +-- validateNamespaceAndKey(namespace, key)
//     `-- vaultProviders

import * as path from "node:path";
import { ok, fail, type Result } from "../results/Result.js";
import { ConfigurationDocumentLocalFile } from "./ConfigurationDocumentLocalFile.js";
import { OAuthTokenManager } from "../oauth/OAuthTokenManager.js";
import { PendingJournalManager } from "../recovery/PendingJournalManager.js";
import type { ConfigurationValue } from "./ConfigurationValue.js";
import type { IConfigurationCipher } from "../encryption/IConfigurationCipher.js";
import type { IVaultProvider } from "../vault/IVaultProvider.js";

/**
 * ConfigurationEntryResolver encapsulates provider normalization, cipher sealing/unsealing,
 * external vault secret resolution, OAuth token refresh delegation, and pending journal replay.
 */
export class ConfigurationEntryResolver {
  public readonly vaultProviders: ReadonlyMap<string, IVaultProvider>;
  public readonly oauthManager: OAuthTokenManager;
  public readonly journalManager: PendingJournalManager;

  private readonly _configurationDirectory: string;
  private readonly _lockTimeoutMs: number;
  private readonly _cipher?: IConfigurationCipher | undefined;
  private readonly _vaultCredentials: ReadonlyMap<string, { readonly namespace: string; readonly key: string }>;
  private _documentWriteInterceptor?: ((namespace: string) => Result<void>) | undefined;

  public constructor(
    configurationDirectory: string,
    lockTimeoutMs: number,
    cipher: IConfigurationCipher | undefined,
    vaultProviders: ReadonlyMap<string, IVaultProvider>,
    vaultCredentials: ReadonlyMap<string, { readonly namespace: string; readonly key: string }>,
    oauthManager: OAuthTokenManager,
    journalManager: PendingJournalManager
  ) {
    this._configurationDirectory = configurationDirectory;
    this._lockTimeoutMs = lockTimeoutMs;
    this._cipher = cipher;
    this.vaultProviders = vaultProviders;
    this._vaultCredentials = vaultCredentials;
    this.oauthManager = oauthManager;
    this.journalManager = journalManager;
  }

  public setDocumentWriteInterceptor(interceptor?: (namespace: string) => Result<void>): void {
    this._documentWriteInterceptor = interceptor;
  }

  public invokeWriteInterceptor(namespace: string): Result<void> {
    return this._documentWriteInterceptor ? this._documentWriteInterceptor(namespace) : ok(undefined);
  }

  public getDocumentPath(namespace: string): string {
    return path.join(this._configurationDirectory, `${namespace}.json`);
  }

  public getDocument(namespace: string): ConfigurationDocumentLocalFile {
    return new ConfigurationDocumentLocalFile(this.getDocumentPath(namespace), this._lockTimeoutMs);
  }

  public validateNamespaceAndKey(namespace: string, key: string): Result<void> {
    const nsRes = ConfigurationDocumentLocalFile.validateSegment(namespace, "namespace");
    return nsRes.ok ? ConfigurationDocumentLocalFile.validateSegment(key, "key") : nsRes;
  }

  public normalizeProviderName(entry?: ConfigurationValue): string {
    if (!entry) return "Default";
    if (entry.provider && entry.provider.trim().length > 0 && entry.provider !== "Default") {
      return entry.provider;
    }
    return entry.encrypted ? "aesLocal" : "Default";
  }

  public canonicalProvider(provider?: string): string {
    if (!provider || provider.trim().length === 0 || provider === "Default") {
      return "Default";
    }
    return provider.trim();
  }

  public checkCircularCredential(providerName: string, namespace: string, key: string): Result<void> {
    if (providerName === "Default" || providerName === "aesLocal" || providerName === "oauth") {
      return ok(undefined);
    }
    const cred = this._vaultCredentials.get(providerName);
    if (cred && cred.namespace === namespace && cred.key === key) {
      return fail(
        `configuration: circular credential: '${namespace}/${key}' is the credential provider '${providerName}' authenticates with, so it cannot be stored in '${providerName}' — reading the vault would require reading the vault`,
        "Conflict"
      );
    }
    return ok(undefined);
  }

  public async resolveEntryValue(
    namespace: string,
    key: string,
    entry: ConfigurationValue,
    signal?: AbortSignal
  ): Promise<Result<unknown>> {
    signal?.throwIfAborted();
    const providerName = this.normalizeProviderName(entry);

    if (providerName === "Default") {
      if (entry.json && typeof entry.value === "string") {
        return this.parseJsonValue(namespace, key, entry.value);
      }
      return ok(entry.value);
    }

    if (providerName === "aesLocal" || providerName === "oauth") {
      if (!this._cipher) {
        return fail("Cannot decrypt entry: no cipher configured.", "Unavailable");
      }
      if (typeof entry.value !== "string") {
        return fail("Encrypted entry must carry a string payload.", "Validation");
      }

      const openRes = this._cipher.open(entry.value, namespace, key);
      if (!openRes.ok) return openRes;

      if (providerName === "oauth" || OAuthTokenManager.isOAuthGrantPayload(openRes.value)) {
        const doc = this.getDocument(namespace);
        return this.oauthManager.resolveGrantAccessToken(
          namespace,
          key,
          entry,
          async (updatedEntry) => {
            const interceptRes = this.invokeWriteInterceptor(namespace);
            if (!interceptRes.ok) return interceptRes;
            return doc.modify((curr) => {
              curr[key] = updatedEntry;
              return ok(curr);
            }, signal);
          },
          signal
        );
      }

      return entry.json ? this.parseJsonValue(namespace, key, openRes.value) : openRes;
    }

    const vault = this.vaultProviders.get(providerName);
    if (!vault) {
      return fail(`Configuration provider '${providerName}' is not registered.`, "Validation");
    }

    const secretName = typeof entry.value === "string" ? entry.value : `${namespace}/${key}`;
    const secretRes = await vault.getSecret(secretName, signal);
    if (!secretRes.ok) return secretRes;

    return entry.json ? this.parseJsonValue(namespace, key, secretRes.value) : secretRes;
  }

  public async prepareEntryForProvider(
    namespace: string,
    key: string,
    value: unknown,
    providerName: string,
    signal?: AbortSignal
  ): Promise<
    Result<{
      readonly entry: ConfigurationValue;
      readonly vaultWrite?:
        | { readonly provider: string; readonly secretName: string; readonly value: string }
        | undefined;
    }>
  > {
    signal?.throwIfAborted();
    if (providerName === "Default") {
      return ok({ entry: { value } });
    }

    const isJson = typeof value !== "string";
    const serializedString = isJson ? JSON.stringify(value) : (value as string);

    if (providerName === "aesLocal" || providerName === "oauth") {
      if (!this._cipher) {
        return fail("Cannot encrypt entry: no cipher configured.", "Unavailable");
      }
      const sealRes = this._cipher.seal(serializedString, namespace, key);
      if (!sealRes.ok) return sealRes;
      return ok({
        entry: {
          value: sealRes.value,
          encrypted: true,
          provider: providerName === "oauth" ? "oauth" : "aesLocal",
          ...(isJson ? { json: true } : {})
        }
      });
    }

    if (!this.vaultProviders.has(providerName)) {
      return fail(`Configuration provider '${providerName}' is not registered.`, "Validation");
    }

    const pointer = `${namespace}/${key}`;
    return ok({
      entry: {
        value: pointer,
        encrypted: true,
        provider: providerName,
        ...(isJson ? { json: true } : {})
      },
      vaultWrite: { provider: providerName, secretName: pointer, value: serializedString }
    });
  }

  public async replayPendingJournalsIfPresent(
    namespace: string,
    signal?: AbortSignal,
    expectedOperationId?: string
  ): Promise<Result<void>> {
    signal?.throwIfAborted();

    if (this.journalManager.hasMoveJournal(namespace)) {
      const moveRead = this.journalManager.readMoveJournal(namespace);
      if (!moveRead.ok) return moveRead;
      const moveRecord = moveRead.value;
      if (moveRecord) {
        const vault = this.vaultProviders.get(moveRecord.sourceProvider);
        if (vault) {
          const delRes = await vault.deleteSecret(moveRecord.sourceSecretName, signal);
          if (!delRes.ok) return delRes;
        }
        this.journalManager.deleteMoveJournal(namespace);
      }
    }

    if (!this.journalManager.hasWriteJournal(namespace)) {
      return ok(undefined);
    }

    const doc = this.getDocument(namespace);
    const docPath = this.getDocumentPath(namespace);

    return doc.modify(async (current) => {
      const journalRead = this.journalManager.readWriteJournal(namespace);
      if (!journalRead.ok) return journalRead;
      const journal = journalRead.value;
      if (!journal) return ok(current);

      if (expectedOperationId !== undefined && journal.operationId !== expectedOperationId) {
        return fail(
          `Pending journal operationId '${journal.operationId}' does not match recovery handle '${expectedOperationId}'.`,
          "Conflict"
        );
      }

      const currentSha256 = PendingJournalManager.computeDocumentSha256(docPath);

      if (currentSha256 === journal.plannedNewSha256) {
        for (const vw of journal.vaultWrites) {
          const vault = this.vaultProviders.get(vw.provider);
          if (!vault) return fail(`Configuration provider '${vw.provider}' is not registered.`, "Validation");
          const setRes = await vault.setSecret(vw.secretName, vw.value, signal);
          if (!setRes.ok) return setRes;
        }
        this.journalManager.deleteWriteJournal(namespace);
        return ok(current);
      }

      if (currentSha256 !== journal.expectedExistingSha256) {
        return fail(
          `Configuration in namespace '${namespace}' changed after the interrupted operation started. Recovery stopped to prevent overwriting concurrent changes.`,
          "Conflict"
        );
      }

      if (journal.stage === "vault") {
        for (const vw of journal.vaultWrites) {
          signal?.throwIfAborted();
          const vault = this.vaultProviders.get(vw.provider);
          if (!vault) return fail(`Configuration provider '${vw.provider}' is not registered.`, "Validation");
          const setRes = await vault.setSecret(vw.secretName, vw.value, signal);
          if (!setRes.ok) return setRes;
        }
        this.journalManager.updateWriteJournalStage(namespace, "local");
      }

      const interceptRes = this.invokeWriteInterceptor(namespace);
      if (!interceptRes.ok) return interceptRes;

      this.journalManager.deleteWriteJournal(namespace);
      return ok(journal.plannedDocument);
    }, signal);
  }

  private parseJsonValue(namespace: string, key: string, raw: string): Result<unknown> {
    try {
      return ok(JSON.parse(raw));
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err);
      return fail(`Failed to deserialize JSON value for '${namespace}/${key}': ${message}`, "Validation");
    }
  }
}
