// Responsibility: Read and validate application sections from the configuration manifest.
// Public interface:
// +-- ConfigurationManifestReader
// |   +-- DEFAULT_MANIFEST_FILENAME
// |   +-- DEFAULT_MANIFEST_FOLDER
// |   +-- defaultManifestPath()
// |   +-- readApplication(manifestPath, applicationName)
// |   +-- readManifest(manifestPath)
// |   `-- resolveDirectory(manifestPath, configurationDirectory)

import * as fs from "node:fs";
import * as path from "node:path";
import * as os from "node:os";
import { ok, fail, type Result } from "../results/Result.js";
import { TimeSpanParser } from "./TimeSpanParser.js";
import type { ConfigurationManifest, ApplicationManifestEntry } from "./ConfigurationManifest.js";

export interface ResolvedApplicationSettings {
  readonly configurationDirectory: string;
  readonly lockTimeoutMs: number;
  readonly keyVault?: ApplicationManifestEntry["keyVault"];
}

/**
 * ConfigurationManifestReader locates and parses configuration manifest files.
 */
export class ConfigurationManifestReader {
  public static readonly DEFAULT_MANIFEST_FILENAME = "config-manifest.json";
  public static readonly DEFAULT_MANIFEST_FOLDER = ".nyssa-ai";
  public static readonly DEFAULT_LOCK_TIMEOUT_MS = 10000;

  /**
   * Returns the user's default manifest path (~/.nyssa-ai/config-manifest.json).
   */
  public static defaultManifestPath(): string {
    return path.join(os.homedir(), this.DEFAULT_MANIFEST_FOLDER, this.DEFAULT_MANIFEST_FILENAME);
  }

  /**
   * Reads and parses a ConfigurationManifest file.
   */
  public static readManifest(manifestPath: string): Result<ConfigurationManifest> {
    const resolved = path.resolve(manifestPath);
    if (!fs.existsSync(resolved)) {
      return fail(`Manifest file not found: '${resolved}'`);
    }

    try {
      const raw = fs.readFileSync(resolved, "utf8");
      const parsed = JSON.parse(raw) as ConfigurationManifest;
      return ok(parsed);
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err);
      return fail(`Failed to read manifest at '${resolved}': ${message}`);
    }
  }

  /**
   * Resolves a configuration directory: relative paths resolve relative to the manifest directory.
   */
  public static resolveDirectory(manifestPath: string, configurationDirectory: string): string {
    if (path.isAbsolute(configurationDirectory)) {
      return path.resolve(configurationDirectory);
    }
    return path.resolve(path.dirname(manifestPath), configurationDirectory);
  }

  /**
   * Reads the configuration section for one application.
   */
  public static readApplication(
    manifestPath: string,
    applicationName: string
  ): Result<ResolvedApplicationSettings> {
    const manifestResult = this.readManifest(manifestPath);
    if (!manifestResult.ok) {
      return manifestResult;
    }

    const manifest = manifestResult.value;
    const section = manifest.applications?.[applicationName];
    if (!section) {
      return fail(`Manifest '${manifestPath}' has no section for '${applicationName}'.`);
    }

    if (!section.configurationDirectory || section.configurationDirectory.trim().length === 0) {
      return fail(`Manifest section for '${applicationName}' does not name a configurationDirectory.`);
    }

    let lockTimeoutMs = this.DEFAULT_LOCK_TIMEOUT_MS;
    if (section.lockTimeout) {
      const parseRes = TimeSpanParser.parse(section.lockTimeout);
      if (!parseRes.ok) {
        return parseRes;
      }
      lockTimeoutMs = parseRes.value;
    }

    const resolvedDir = this.resolveDirectory(manifestPath, section.configurationDirectory);

    return ok({
      configurationDirectory: resolvedDir,
      lockTimeoutMs,
      keyVault: section.keyVault
    });
  }
}
