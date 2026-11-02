// Responsibility: Factory instantiating VaultProviderBitwarden instances for Bitwarden Secrets Manager (bws).
// Public interface:
// `-- VaultProviderFactoryBitwarden
//     +-- DEFAULT_KIND
//     +-- LEGACY_KIND
//     +-- SHORT_KIND
//     +-- authorization(options)
//     +-- create(options, getCredential)
//     +-- kind
//     `-- new(kind, commandRunner)

import { ok, fail, type Result } from "@nyssa-ai/result";
import { VaultProviderBitwarden } from "./VaultProviderBitwarden.js";
import type { VaultOptions } from "../../manifest/VaultOptions.js";
import type { IVaultProvider } from "../IVaultProvider.js";
import type { IVaultProviderFactory } from "../IVaultProviderFactory.js";
import type { OAuthOptions } from "../../oauth/OAuthOptions.js";
import type { BwsCommandRunner } from "./BwsCommandRunner.js";

/**
 * VaultProviderFactoryBitwarden builds VaultProviderBitwarden instances from VaultOptions.
 */
export class VaultProviderFactoryBitwarden {
  public static readonly DEFAULT_KIND = "bitwarden-secrets";
  public static readonly SHORT_KIND = "bws";
  public static readonly LEGACY_KIND = "bitwarden";

  public readonly kind: string;
  private readonly _runner: BwsCommandRunner | undefined;

  public constructor(kind: string = VaultProviderFactoryBitwarden.DEFAULT_KIND, commandRunner?: BwsCommandRunner) {
    this.kind = kind;
    this._runner = commandRunner;
  }

  public create(
    options: VaultOptions,
    getCredential: (signal?: AbortSignal) => Promise<Result<string>>
  ): Result<IVaultProvider> {
    if (!options.name || options.name.trim().length === 0) {
      return fail("vault: VaultOptions.name must not be blank.", "Validation");
    }

    const secrets = options.secrets ?? {};
    const hasStaticToken =
      (secrets["accessToken"] !== undefined && secrets["accessToken"].trim().length > 0) ||
      (secrets["machineAccessToken"] !== undefined && secrets["machineAccessToken"].trim().length > 0);
    const hasCredential = options.credential !== undefined && options.credential.trim().length > 0;

    if (!hasStaticToken && !hasCredential) {
      return fail(
        `vault: '${options.name}' requires either keyVault.credential or secrets['accessToken'].`,
        "Validation"
      );
    }

    return ok(new VaultProviderBitwarden(options, getCredential, this._runner));
  }

  public authorization(options: VaultOptions): Result<OAuthOptions | undefined> {
    const settings = options.settings ?? {};
    const authorizationEndpoint =
      settings["authorizationEndpoint"]?.trim() || "https://identity.bitwarden.com/connect/authorize";
    const tokenEndpoint =
      settings["tokenEndpoint"]?.trim() || "https://identity.bitwarden.com/connect/token";
    const clientId = settings["clientId"]?.trim() || "bitwarden-secrets-cli";

    return ok({
      authorizationEndpoint,
      tokenEndpoint,
      clientId
    });
  }
}

const _typeCheck: IVaultProviderFactory = null as unknown as VaultProviderFactoryBitwarden;
void _typeCheck;
