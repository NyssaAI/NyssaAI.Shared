// Responsibility: Read and validate root keyVaults and application sections from the configuration manifest.
// Public interface:
// `-- ConfigurationManifestReader
//     +-- DEFAULT_LOCK_TIMEOUT_MS
//     +-- DEFAULT_MANIFEST_FILENAME
//     +-- DEFAULT_MANIFEST_FOLDER
//     +-- SUPPORTED_VERSION
//     +-- defaultManifestPath(homeDirectory)
//     +-- readApplication(manifestPath, applicationName)
//     +-- readManifest(manifestPath)
//     `-- resolveDirectory(manifestPath, configurationDirectory)

import * as fs from "node:fs";
import * as path from "node:path";
import * as os from "node:os";
import { ok, fail, type Result } from "@nyssa-ai/result";
import { DirectorySafety } from "../fileSecurity/DirectorySafety.js";
import { TimeSpanParser } from "./TimeSpanParser.js";
import { ManifestVaultValidator } from "./ManifestVaultValidator.js";
import type { ConfigurationManifest } from "./ConfigurationManifest.js";
import type { ApplicationManifestEntry } from "./ApplicationManifestEntry.js";
import type { VaultManifestEntry } from "./VaultManifestEntry.js";
import type { ResolvedApplicationSettings } from "./ResolvedApplicationSettings.js";

/**
 * ConfigurationManifestReader locates and parses configuration manifest files.
 */
export class ConfigurationManifestReader {
  public static readonly DEFAULT_MANIFEST_FILENAME = "config-manifest.json";
  public static readonly DEFAULT_MANIFEST_FOLDER = ".nyssa-ai";
  public static readonly DEFAULT_LOCK_TIMEOUT_MS = 10000;
  public static readonly SUPPORTED_VERSION = 1;

  /**
   * Returns the user's default manifest path (<home>/.nyssa-ai/config-manifest.json).
   */
  public static defaultManifestPath(homeDirectory?: string): string {
    const home = homeDirectory || os.homedir();
    return path.join(home, this.DEFAULT_MANIFEST_FOLDER, this.DEFAULT_MANIFEST_FILENAME);
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
   * Reads and parses a ConfigurationManifest file, enforcing private-file permissions and schema rules.
   */
  public static readManifest(manifestPath: string): Result<ConfigurationManifest> {
    const resolved = path.resolve(manifestPath);
    if (!fs.existsSync(resolved)) {
      return fail(`manifest: no manifest at '${manifestPath}'`, "NotFound");
    }

    const fileSafe = DirectorySafety.verifyFile(manifestPath);
    if (!fileSafe.ok) {
      return fileSafe;
    }

    let raw: string;
    try {
      raw = fs.readFileSync(resolved, "utf8");
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err);
      return fail(`manifest: reading '${manifestPath}' failed: ${message}`, "Forbidden");
    }

    let parsed: unknown;
    try {
      const stripped = this.stripJsonCommentsAndTrailingCommas(raw);
      parsed = JSON.parse(stripped) as unknown;
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err);
      return fail(`manifest: '${manifestPath}' failed validation: ${message}`, "Validation");
    }

    if (typeof parsed !== "object" || parsed === null || Array.isArray(parsed)) {
      return fail(`manifest: '${manifestPath}' failed validation: root must be a JSON object`, "Validation");
    }

    const root = parsed as Record<string, unknown>;
    if (ManifestVaultValidator.hasAnySecrets(root)) {
      const privateSafe = DirectorySafety.verifyPrivateFile(manifestPath);
      if (!privateSafe.ok) {
        return privateSafe;
      }
    }

    if (root["version"] !== this.SUPPORTED_VERSION) {
      return fail(
        `manifest: '${manifestPath}' is not a version ${this.SUPPORTED_VERSION} manifest`,
        "Validation"
      );
    }

    const rawApps = root["applications"];
    if (rawApps !== undefined && rawApps !== null && (typeof rawApps !== "object" || Array.isArray(rawApps))) {
      return fail(
        `manifest: '${manifestPath}' failed validation: 'applications' must be an object`,
        "Validation"
      );
    }

    const rawKeyVaults = root["keyVaults"];
    if (
      rawKeyVaults !== undefined &&
      rawKeyVaults !== null &&
      (typeof rawKeyVaults !== "object" || Array.isArray(rawKeyVaults))
    ) {
      return fail(
        `manifest: '${manifestPath}' failed validation: 'keyVaults' must be an object`,
        "Validation"
      );
    }

    const normalizedApps: Record<string, ApplicationManifestEntry> = {};
    for (const [appName, rawSection] of Object.entries((rawApps as Record<string, unknown> | undefined) ?? {})) {
      if (!rawSection || typeof rawSection !== "object" || Array.isArray(rawSection)) {
        return fail(
          `manifest: applications.${appName} in '${manifestPath}' must be an object`,
          "Validation"
        );
      }
      const sectionObj = rawSection as Record<string, unknown>;
      if (sectionObj["keyVault"] !== undefined && sectionObj["keyVault"] !== null) {
        return fail(
          `manifest: applications.${appName}.keyVault is no longer supported; move vault definitions to root 'keyVaults'`,
          "Validation"
        );
      }
      const { keyVault: _ignored, ...rest } = sectionObj;
      void _ignored;
      normalizedApps[appName] = rest as ApplicationManifestEntry;
    }

    const normalizedVaults: Record<string, VaultManifestEntry> = {};
    for (const [vaultName, rawVault] of Object.entries((rawKeyVaults as Record<string, unknown> | undefined) ?? {})) {
      const entryRes = ManifestVaultValidator.validateManifestEntry(vaultName, rawVault);
      if (!entryRes.ok) {
        return entryRes;
      }
      normalizedVaults[vaultName] = entryRes.value;
    }

    return ok({
      version: this.SUPPORTED_VERSION,
      keyVaults: normalizedVaults,
      applications: normalizedApps
    });
  }

  /**
   * Reads the configuration section for one application, returning its settled settings and root keyVaults.
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
      return fail(
        `manifest: '${manifestPath}' has no section for '${applicationName}'; run the application's init command, or add applications.${applicationName}.configurationDirectory`,
        "NotFound"
      );
    }

    if (!section.configurationDirectory || section.configurationDirectory.trim().length === 0) {
      return fail(
        `manifest: the section for '${applicationName}' in '${manifestPath}' names no configurationDirectory`,
        "NotFound"
      );
    }

    let lockTimeoutMs = this.DEFAULT_LOCK_TIMEOUT_MS;
    if (section.lockTimeoutMs !== undefined && section.lockTimeoutMs !== null) {
      if (typeof section.lockTimeoutMs !== "number" || section.lockTimeoutMs <= 0) {
        return fail(
          `manifest: '${manifestPath}' names lockTimeoutMs '${section.lockTimeoutMs}' for '${applicationName}', which is not a positive integer`,
          "Validation"
        );
      }
      lockTimeoutMs = Math.round(section.lockTimeoutMs);
    } else if (section.lockTimeout !== undefined && section.lockTimeout !== null) {
      if (typeof section.lockTimeout === "number" && section.lockTimeout > 0) {
        lockTimeoutMs = Math.round(section.lockTimeout);
      } else if (typeof section.lockTimeout === "string") {
        const parseRes = TimeSpanParser.parse(section.lockTimeout);
        if (!parseRes.ok) {
          return fail(
            `manifest: '${manifestPath}' names lockTimeout '${section.lockTimeout}' for '${applicationName}', which is not a positive TimeSpan`,
            "Validation"
          );
        }
        lockTimeoutMs = parseRes.value;
      } else {
        return fail(
          `manifest: '${manifestPath}' names invalid lockTimeout for '${applicationName}'`,
          "Validation"
        );
      }
    }

    return ok({
      configurationDirectory: this.resolveDirectory(manifestPath, section.configurationDirectory),
      lockTimeoutMs,
      keyVaults: manifest.keyVaults ?? {}
    });
  }

  /**
   * Strips // line comments, /* block comments * /, and trailing commas outside string literals.
   */
  private static stripJsonCommentsAndTrailingCommas(input: string): string {
    let out = "";
    let inString = false;
    let escaped = false;
    for (let i = 0; i < input.length; i++) {
      const ch = input[i]!;
      const next = input[i + 1];
      if (inString) {
        out += ch;
        if (escaped) escaped = false;
        else if (ch === "\\") escaped = true;
        else if (ch === "\"") inString = false;
      } else if (ch === "\"") {
        inString = true;
        out += ch;
      } else if (ch === "/" && next === "/") {
        i += 2;
        while (i < input.length && input[i] !== "\n") i++;
        i--;
      } else if (ch === "/" && next === "*") {
        i += 2;
        while (i < input.length && !(input[i] === "*" && input[i + 1] === "/")) i++;
        i++;
      } else {
        out += ch;
      }
    }

    let finalOut = "";
    inString = false;
    escaped = false;
    for (let j = 0; j < out.length; j++) {
      const ch = out[j]!;
      if (inString) {
        finalOut += ch;
        if (escaped) escaped = false;
        else if (ch === "\\") escaped = true;
        else if (ch === "\"") inString = false;
      } else if (ch === "\"") {
        inString = true;
        finalOut += ch;
      } else if (ch === ",") {
        let k = j + 1;
        while (k < out.length && /\s/.test(out[k]!)) k++;
        if (k < out.length && (out[k] === "}" || out[k] === "]")) continue;
        finalOut += ch;
      } else {
        finalOut += ch;
      }
    }
    return finalOut;
  }
}
