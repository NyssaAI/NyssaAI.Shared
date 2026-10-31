// Responsibility: Carries configuration settings and validation for the database gateway.
// Public interface:
// +-- DbGatewayConfiguration
// |   +-- commandTimeoutSeconds
// |   +-- connectionString
// |   `-- databaseProvider
// `-- validateDbGatewayConfiguration(config)

export interface DbGatewayConfiguration {
  readonly databaseProvider: string;
  readonly connectionString: string;
  readonly commandTimeoutSeconds: number;
}

export function validateDbGatewayConfiguration(config: DbGatewayConfiguration): void {
  if (!config) {
    throw new Error("DbGatewayConfiguration is required.");
  }
  if (!config.databaseProvider || typeof config.databaseProvider !== "string" || !config.databaseProvider.trim()) {
    throw new Error("DbGatewayConfiguration.databaseProvider must be a non-empty string.");
  }
  if (!config.connectionString || typeof config.connectionString !== "string" || !config.connectionString.trim()) {
    throw new Error("DbGatewayConfiguration.connectionString must be a non-empty string.");
  }
  if (typeof config.commandTimeoutSeconds !== "number" || config.commandTimeoutSeconds < 0 || isNaN(config.commandTimeoutSeconds)) {
    throw new RangeError("DbGatewayConfiguration.commandTimeoutSeconds must be a non-negative number.");
  }
}
