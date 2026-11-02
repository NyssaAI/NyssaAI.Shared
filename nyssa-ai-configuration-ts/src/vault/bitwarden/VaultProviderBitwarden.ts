// Responsibility: External cloud key vault provider backed by the Bitwarden Secrets Manager CLI (bws).
// Public interface:
// `-- VaultProviderBitwarden
//     +-- ACCESS_TOKEN_ENV
//     +-- DEFAULT_BINARY
//     +-- ORGANIZATION_ID_ENV
//     +-- SERVER_URL_ENV
//     +-- deleteSecret(secretName, signal)
//     +-- getSecret(secretName, signal)
//     +-- name
//     +-- new(options, getCredential, commandRunner)
//     +-- recovery
//     `-- setSecret(secretName, secretValue, signal)

import { execFile } from "node:child_process";
import { ok, fail, type Result } from "@nyssa-ai/result";
import type { VaultOptions } from "../../manifest/VaultOptions.js";
import type { IVaultProvider } from "../IVaultProvider.js";
import type { CloudKeyVaultRecovery } from "../CloudKeyVaultRecovery.js";
import type { BwsSecretEntry } from "./Types.js";
import type { BwsCommandRunner } from "./BwsCommandRunner.js";

/**
 * VaultProviderBitwarden implements IVaultProvider by invoking the Bitwarden Secrets Manager CLI (`bws`).
 */
export class VaultProviderBitwarden {
  public static readonly DEFAULT_BINARY = "bws";
  public static readonly ACCESS_TOKEN_ENV = "BWS_ACCESS_TOKEN";
  public static readonly ORGANIZATION_ID_ENV = "BWS_ORGANIZATION_ID";
  public static readonly SERVER_URL_ENV = "BWS_SERVER_URL";

  public readonly name: string;
  public readonly recovery: CloudKeyVaultRecovery | undefined = undefined;

  private readonly _projectId: string | undefined;
  private readonly _organizationId: string | undefined;
  private readonly _serverUrl: string | undefined;
  private readonly _binaryPath: string;
  private readonly _staticAccessToken: string | undefined;
  private readonly _hasCredential: boolean;
  private readonly _getCredential: ((signal?: AbortSignal) => Promise<Result<string>>) | undefined;
  private readonly _runner: BwsCommandRunner;

  public constructor(
    options: VaultOptions,
    getCredential?: (signal?: AbortSignal) => Promise<Result<string>>,
    commandRunner?: BwsCommandRunner
  ) {
    this.name = options.name;
    const settings = options.settings ?? {};
    const secrets = options.secrets ?? {};
    this._projectId = VaultProviderBitwarden.readNonBlank(settings["projectId"]);
    this._organizationId = VaultProviderBitwarden.readNonBlank(settings["organizationId"]);
    this._serverUrl =
      VaultProviderBitwarden.readNonBlank(options.endpoint) ??
      VaultProviderBitwarden.readNonBlank(settings["serverUrl"]) ??
      VaultProviderBitwarden.readNonBlank(settings["apiUrl"]);
    this._binaryPath =
      VaultProviderBitwarden.readNonBlank(settings["cliPath"]) ??
      VaultProviderBitwarden.readNonBlank(settings["bwsCliPath"]) ??
      VaultProviderBitwarden.DEFAULT_BINARY;
    this._staticAccessToken =
      VaultProviderBitwarden.readNonBlank(secrets["accessToken"]) ??
      VaultProviderBitwarden.readNonBlank(secrets["machineAccessToken"]);
    this._hasCredential =
      VaultProviderBitwarden.readNonBlank(options.credential) !== undefined && getCredential !== undefined;
    this._getCredential = getCredential;
    this._runner = commandRunner ?? VaultProviderBitwarden.defaultCommandRunner;
  }

  public async getSecret(secretName: string, signal?: AbortSignal): Promise<Result<string>> {
    signal?.throwIfAborted();
    const envRes = await this.resolveEnvironment(signal);
    if (!envRes.ok) return envRes;

    const listRes = await this.listSecrets(envRes.value, signal);
    if (!listRes.ok) return listRes;

    const match = VaultProviderBitwarden.findMatchingItem(listRes.value, secretName);
    if (!match || !match.id) {
      return fail(
        `vault: '${secretName}' was not found in Bitwarden Secrets Manager provider '${this.name}'`,
        "NotFound"
      );
    }

    const outRes = await this._runner(
      this._binaryPath,
      ["secret", "get", match.id, "--output", "json"],
      envRes.value,
      signal
    );
    if (!outRes.ok) return outRes;

    try {
      const parsed = JSON.parse(outRes.value) as BwsSecretEntry;
      if (typeof parsed?.value !== "string") {
        return fail(`vault: '${this.name}' returned a secret payload without a string value.`, "Unavailable");
      }
      return ok(parsed.value);
    } catch (err) {
      const msg = err instanceof Error ? err.message : String(err);
      return fail(`vault: '${this.name}' returned invalid JSON: ${msg}`, "Unavailable");
    }
  }

  public async setSecret(secretName: string, secretValue: string, signal?: AbortSignal): Promise<Result<void>> {
    signal?.throwIfAborted();
    const envRes = await this.resolveEnvironment(signal);
    if (!envRes.ok) return envRes;

    const listRes = await this.listSecrets(envRes.value, signal);
    if (!listRes.ok) return listRes;

    const existing = VaultProviderBitwarden.findMatchingItem(listRes.value, secretName);
    let args: readonly string[];
    if (existing?.id) {
      args = ["secret", "edit", existing.id, "--value", secretValue, "--output", "json"];
    } else {
      if (!this._projectId) {
        return fail(
          `vault: '${this.name}' requires settings['projectId'] to create secrets in Bitwarden Secrets Manager.`,
          "Validation"
        );
      }
      args = ["secret", "create", secretName, secretValue, this._projectId, "--output", "json"];
    }

    const writeRes = await this._runner(this._binaryPath, args, envRes.value, signal);
    return writeRes.ok ? ok(undefined) : writeRes;
  }

  public async deleteSecret(
    secretName: string,
    signal?: AbortSignal
  ): Promise<Result<{ readonly deleted: boolean; readonly retained: boolean }>> {
    signal?.throwIfAborted();
    const envRes = await this.resolveEnvironment(signal);
    if (!envRes.ok) return envRes;

    const listRes = await this.listSecrets(envRes.value, signal);
    if (!listRes.ok) return listRes;

    const existing = VaultProviderBitwarden.findMatchingItem(listRes.value, secretName);
    if (!existing?.id) {
      return ok({ deleted: false, retained: false });
    }

    const delRes = await this._runner(this._binaryPath, ["secret", "delete", existing.id], envRes.value, signal);
    return delRes.ok ? ok({ deleted: true, retained: false }) : delRes;
  }

  private async resolveEnvironment(signal?: AbortSignal): Promise<Result<Readonly<Record<string, string>>>> {
    let token = this._staticAccessToken;
    if (this._hasCredential && this._getCredential) {
      const credRes = await this._getCredential(signal);
      if (!credRes.ok) return credRes;
      token = credRes.value;
    }

    if (!token || token.trim().length === 0) {
      return fail(
        `vault: '${this.name}' requires either keyVault.credential or secrets['accessToken'].`,
        "Validation"
      );
    }

    const env: Record<string, string> = {
      [VaultProviderBitwarden.ACCESS_TOKEN_ENV]: token
    };
    if (this._organizationId) {
      env[VaultProviderBitwarden.ORGANIZATION_ID_ENV] = this._organizationId;
    }
    if (this._serverUrl) {
      env[VaultProviderBitwarden.SERVER_URL_ENV] = this._serverUrl;
    }
    return ok(env);
  }

  private async listSecrets(
    env: Readonly<Record<string, string>>,
    signal?: AbortSignal
  ): Promise<Result<readonly BwsSecretEntry[]>> {
    const args = this._projectId
      ? ["secret", "list", this._projectId, "--output", "json"]
      : ["secret", "list", "--output", "json"];
    const res = await this._runner(this._binaryPath, args, env, signal);
    if (!res.ok) {
      if (res.message.includes("404") && res.message.toLowerCase().includes("not found")) {
        return ok([]);
      }
      return res;
    }

    try {
      const parsed = JSON.parse(res.value) as unknown;
      if (!Array.isArray(parsed)) {
        return fail(`vault: '${this.name}' returned non-array JSON for secret list.`, "Unavailable");
      }
      return ok(parsed as readonly BwsSecretEntry[]);
    } catch (err) {
      const msg = err instanceof Error ? err.message : String(err);
      return fail(`vault: '${this.name}' returned invalid JSON for secret list: ${msg}`, "Unavailable");
    }
  }

  private static findMatchingItem(
    items: readonly BwsSecretEntry[],
    secretName: string
  ): BwsSecretEntry | undefined {
    const dotName = secretName.replace("/", ".");
    return items.find((item) => {
      const resolvedKey = item.key ?? item.slug ?? "";
      return resolvedKey === secretName || resolvedKey === dotName || item.id === secretName;
    });
  }

  private static readNonBlank(value?: string): string | undefined {
    const trimmed = value?.trim();
    return trimmed && trimmed.length > 0 ? trimmed : undefined;
  }

  private static defaultCommandRunner(
    binaryPath: string,
    args: readonly string[],
    env: Readonly<Record<string, string>>,
    signal?: AbortSignal
  ): Promise<Result<string>> {
    return new Promise((resolve, reject) => {
      if (signal?.aborted) {
        reject(signal.reason ?? new Error("Operation was aborted."));
        return;
      }
      execFile(
        binaryPath,
        [...args],
        {
          env: { ...process.env, ...env },
          encoding: "utf8",
          ...(signal ? { signal } : {})
        },
        (error, stdout, stderr) => {
          if (signal?.aborted) {
            reject(signal.reason ?? new Error("Operation was aborted."));
            return;
          }
          if (error) {
            const detail = stderr?.trim() || error.message;
            const lower = detail.toLowerCase();
            const category =
              detail.includes("401") ||
              detail.includes("403") ||
              lower.includes("unauthorized") ||
              lower.includes("unauthenticated")
                ? "Forbidden"
                : "Unavailable";
            resolve(fail(`vault: Bitwarden CLI command failed: ${detail}`, category));
            return;
          }
          resolve(ok(stdout));
        }
      );
    });
  }
}

const _typeCheck: IVaultProvider = null as unknown as VaultProviderBitwarden;
void _typeCheck;
