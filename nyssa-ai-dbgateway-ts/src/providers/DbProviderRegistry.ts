// Responsibility: Validate provider registrations and resolve prepared provider factories.
// Public interface:
// `-- DbProviderRegistry
//     `-- registerDatabaseProviders(settings, customRegistrations)

import {
  type DbGatewayConfiguration,
  validateDbGatewayConfiguration
} from "../configuration/DbGatewayConfiguration.js";
import type { DbProvider } from "./DbProvider.js";
import { DbProviderRegistration } from "./DbProviderRegistration.js";
import { DbProviderSqlite } from "./DbProviderSqlite.js";
import { DbProviderPostgres } from "./DbProviderPostgres.js";

export class DbProviderRegistry {
  public static registerDatabaseProviders(
    settings: DbGatewayConfiguration,
    customRegistrations?: readonly DbProviderRegistration[]
  ): () => DbProvider {
    validateDbGatewayConfiguration(settings);

    const defaultRegistrations: DbProviderRegistration[] = [
      new DbProviderRegistration("sqlite", DbProviderSqlite.prepare),
      new DbProviderRegistration("postgresql", DbProviderPostgres.prepare),
      new DbProviderRegistration("postgres", DbProviderPostgres.prepare),
      new DbProviderRegistration("npgsql", DbProviderPostgres.prepare)
    ];

    const allRegistrations = [...defaultRegistrations, ...(customRegistrations ?? [])];
    const requested = settings.databaseProvider.trim().toLowerCase();

    for (const reg of allRegistrations) {
      reg.validate();
      if (reg.databaseProvider === requested) {
        const createFactory = reg.prepare(settings);
        return () => {
          const provider = createFactory();
          if (!provider) {
            throw new Error(`Provider factory for "${requested}" returned null or undefined.`);
          }
          return provider;
        };
      }
    }

    throw new Error(`No database provider is registered for "${settings.databaseProvider}".`);
  }
}
