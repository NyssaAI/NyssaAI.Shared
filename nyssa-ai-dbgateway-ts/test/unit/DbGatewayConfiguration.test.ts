// Responsibility: Unit test configuration validation and connection string password redaction.
// Public interface:
// (none)

import { describe, it, expect } from "vitest";
import {
  type DbGatewayConfiguration,
  validateDbGatewayConfiguration
} from "../../src/configuration/DbGatewayConfiguration.js";
import { redactConnectionString } from "../../src/logging/redactConnectionString.js";

describe("DbGatewayConfiguration", () => {
  it("validateDbGatewayConfiguration_validSettings_passesWithoutError", () => {
    const valid: DbGatewayConfiguration = {
      databaseProvider: "sqlite",
      connectionString: "Data Source=:memory:",
      commandTimeoutSeconds: 30
    };
    expect(() => validateDbGatewayConfiguration(valid)).not.toThrow();
  });

  it("validateDbGatewayConfiguration_invalidSettings_throwsDescriptiveErrors", () => {
    expect(() =>
      validateDbGatewayConfiguration({
        databaseProvider: "",
        connectionString: "Data Source=:memory:",
        commandTimeoutSeconds: 30
      })
    ).toThrow();

    expect(() =>
      validateDbGatewayConfiguration({
        databaseProvider: "sqlite",
        connectionString: "",
        commandTimeoutSeconds: 30
      })
    ).toThrow();

    expect(() =>
      validateDbGatewayConfiguration({
        databaseProvider: "sqlite",
        connectionString: "Data Source=:memory:",
        commandTimeoutSeconds: -5
      })
    ).toThrow();
  });

  it("redactConnectionString_passwordsInKeyValueAndUri_redactsSafely", () => {
    const kv = "Host=127.0.0.1;Port=5432;Database=test;Username=admin;Password=Secret123!;Pooling=False";
    const redactedKv = redactConnectionString(kv);
    expect(redactedKv).toContain("Password=***");
    expect(redactedKv).not.toContain("Secret123!");

    const uri = "postgresql://myuser:SuperSecretPassword@127.0.0.1:5432/mydb?sslmode=disable";
    const redactedUri = redactConnectionString(uri);
    expect(redactedUri).toContain("://myuser:***@127.0.0.1:5432");
    expect(redactedUri).not.toContain("SuperSecretPassword");
  });
});
