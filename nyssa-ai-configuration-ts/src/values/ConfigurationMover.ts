// Responsibility: Safe two-phase provider migration (move) across plaintext, aesLocal, and external vault providers.
// Public interface:
// `-- ConfigurationMover
//     +-- move(namespace, key, destinationProvider, signal)
//     `-- new(resolver)

import { ok, fail, type Result } from "../results/Result.js";
import type { ConfigurationEntryResolver } from "./ConfigurationEntryResolver.js";
import type { ConfigurationMove } from "./ConfigurationMove.js";

/**
 * ConfigurationMover migrates a configuration entry between providers, writing to the destination
 * provider before updating the local namespace document and withdrawing the source copy.
 */
export class ConfigurationMover {
  private readonly _resolver: ConfigurationEntryResolver;

  public constructor(resolver: ConfigurationEntryResolver) {
    this._resolver = resolver;
  }

  public async move(
    namespace: string,
    key: string,
    destinationProvider: string,
    signal?: AbortSignal
  ): Promise<Result<ConfigurationMove>> {
    signal?.throwIfAborted();
    const validRes = this._resolver.validateNamespaceAndKey(namespace, key);
    if (!validRes.ok) {
      return validRes;
    }

    const replayRes = await this._resolver.replayPendingJournalsIfPresent(namespace, signal);
    if (!replayRes.ok) {
      return replayRes;
    }

    const targetProvider = this._resolver.canonicalProvider(destinationProvider);
    const circularRes = this._resolver.checkCircularCredential(targetProvider, namespace, key);
    if (!circularRes.ok) {
      return circularRes;
    }

    if (
      targetProvider !== "Default" &&
      targetProvider !== "aesLocal" &&
      targetProvider !== "oauth" &&
      !this._resolver.vaultProviders.has(targetProvider)
    ) {
      return fail(`Configuration provider '${targetProvider}' is not registered.`, "Validation");
    }

    const doc = this._resolver.getDocument(namespace);
    let sourceProvider = "Default";
    let sourceSecretName = `${namespace}/${key}`;
    let wasNoOp = false;

    const modRes = await doc.modify(async (current) => {
      const existing = current[key];
      if (!existing) {
        return fail(`No configuration entry exists at '${namespace}/${key}'.`, "NotFound");
      }

      sourceProvider = this._resolver.normalizeProviderName(existing);
      if (sourceProvider === targetProvider) {
        wasNoOp = true;
        return ok(current);
      }

      if (
        sourceProvider !== "Default" &&
        sourceProvider !== "aesLocal" &&
        sourceProvider !== "oauth" &&
        typeof existing.value === "string"
      ) {
        sourceSecretName = existing.value;
      }

      const clearRes = await this._resolver.resolveEntryValue(namespace, key, existing, signal);
      if (!clearRes.ok) {
        return clearRes;
      }

      const preparedRes = await this._resolver.prepareEntryForProvider(
        namespace,
        key,
        clearRes.value,
        targetProvider,
        signal
      );
      if (!preparedRes.ok) {
        return preparedRes;
      }

      if (preparedRes.value.vaultWrite) {
        const vw = preparedRes.value.vaultWrite;
        const destVault = this._resolver.vaultProviders.get(vw.provider);
        if (!destVault) {
          return fail(`Configuration provider '${vw.provider}' is not registered.`, "Validation");
        }
        const writeDestRes = await destVault.setSecret(vw.secretName, vw.value, signal);
        if (!writeDestRes.ok) {
          return writeDestRes;
        }
      }

      if (sourceProvider !== "Default" && sourceProvider !== "aesLocal" && sourceProvider !== "oauth") {
        const moveJournalRes = this._resolver.journalManager.createMoveJournal(
          namespace,
          key,
          sourceProvider,
          sourceSecretName
        );
        if (!moveJournalRes.ok) {
          return moveJournalRes;
        }
      }

      const interceptRes = this._resolver.invokeWriteInterceptor(namespace);
      if (!interceptRes.ok) {
        return interceptRes;
      }

      current[key] = preparedRes.value.entry;
      return ok(current);
    }, signal);

    if (!modRes.ok) {
      return modRes;
    }

    if (wasNoOp) {
      return ok({ sourceValueRemains: false });
    }

    if (sourceProvider !== "Default" && sourceProvider !== "aesLocal" && sourceProvider !== "oauth") {
      const sourceVault = this._resolver.vaultProviders.get(sourceProvider);
      if (!sourceVault) {
        return ok({
          sourceValueRemains: true,
          sourceDeleteError: `Source provider '${sourceProvider}' is not registered.`
        });
      }

      const delRes = await sourceVault.deleteSecret(sourceSecretName, signal);
      if (!delRes.ok) {
        this._resolver.journalManager.deleteMoveJournal(namespace);
        return ok({
          sourceValueRemains: true,
          sourceDeleteError: delRes.error
        });
      }

      this._resolver.journalManager.deleteMoveJournal(namespace);
      return ok({
        sourceValueRemains: delRes.value.retained
      });
    }

    return ok({ sourceValueRemains: false });
  }
}
