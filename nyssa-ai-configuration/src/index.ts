// Responsibility: Root exports for the @nyssa-ai/configuration package.
// Public interface:
// +-- ConfigurationClient
// +-- ConfigurationManifestReader
// +-- ConfigurationManifestWriter
// +-- ConfigurationOptions
// +-- IConfiguration
// +-- IConfigurationCipher
// +-- IKeyring
// +-- Result
// +-- fail(error)
// `-- ok(value)

export type { Result } from "./results/Result.js";
export { ok, fail } from "./results/Result.js";

export type { IConfiguration, SetOptions } from "./IConfiguration.js";
export type { ConfigurationOptions } from "./ConfigurationOptions.js";
export { ConfigurationClient } from "./ConfigurationClient.js";

export type { IKeyring } from "./keyring/IKeyring.js";
export { KeyringDefault } from "./keyring/KeyringDefault.js";
export { KeyringProtectedFile } from "./keyring/KeyringProtectedFile.js";

export type { IConfigurationCipher } from "./encryption/IConfigurationCipher.js";
export { ConfigurationCipherAesGcm } from "./encryption/ConfigurationCipherAesGcm.js";

export { ConfigurationManifestReader } from "./manifest/ConfigurationManifestReader.js";
export { ConfigurationManifestWriter } from "./manifest/ConfigurationManifestWriter.js";
export { TimeSpanParser } from "./manifest/TimeSpanParser.js";

export { ProtectedFileSystem } from "./fileSecurity/ProtectedFileSystem.js";
export { DirectorySafety } from "./fileSecurity/DirectorySafety.js";

export type { IConfigurationLogger, LogRecord, LogLevel } from "./logging/IConfigurationLogger.js";
export { NullConfigurationLogger } from "./logging/NullConfigurationLogger.js";
