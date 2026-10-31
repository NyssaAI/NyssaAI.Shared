// Responsibility: Register a compiled database provider factory for a provider key.
// Public interface:
// `-- DbProviderRegistration
//     +-- databaseProvider
//     +-- new(databaseProvider, prepare)
//     +-- prepare(config)
//     `-- validate()

import type { DbGatewayConfiguration } from "../configuration/DbGatewayConfiguration.js";
import type { DbProvider } from "./DbProvider.js";

export class DbProviderRegistration {
  public readonly databaseProvider: string;
  public readonly prepare: (config: DbGatewayConfiguration) => () => DbProvider;

  public constructor(
    databaseProvider: string,
    prepare: (config: DbGatewayConfiguration) => () => DbProvider
  ) {
    this.databaseProvider = databaseProvider ? databaseProvider.trim().toLowerCase() : "";
    this.prepare = prepare;
  }

  public validate(): void {
    if (!this.databaseProvider) {
      throw new Error("Provider registration requires a non-empty databaseProvider.");
    }
    if (!this.prepare || typeof this.prepare !== "function") {
      throw new Error("Provider registration requires a valid prepare callback.");
    }
  }
}
