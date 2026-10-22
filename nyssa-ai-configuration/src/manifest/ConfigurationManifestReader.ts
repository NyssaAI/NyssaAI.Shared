// Responsibility: Read and validate application sections from the configuration manifest.
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
import { ok, fail, type Result } from "../results/Result.js";
import { DirectorySafety } from "../fileSecurity/DirectorySafety.js";
import { TimeSpanParser } from "./TimeSpanParser.js";
import type { ConfigurationManifest } from "./ConfigurationManifest.js";
import type { ApplicationManifestEntry } from "./ApplicationManifestEntry.js";
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
   * Reads and parses a ConfigurationManifest file, verifying file safety and normalizing sections.
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

    try {
      const stripped = this.stripJsonCommentsAndTrailingCommas(raw);
      const parsed = JSON.parse(stripped) as unknown;
      if (typeof parsed !== "object" || parsed === null || Array.isArray(parsed)) {
        return fail(`manifest: '${manifestPath}' failed validation: root must be a JSON object`, "Validation");
      }

      const candidate = parsed as { version?: unknown; applications?: unknown };
      if (candidate.version !== this.SUPPORTED_VERSION) {
        return fail(
          `manifest: '${manifestPath}' is not a version ${this.SUPPORTED_VERSION} manifest`,
          "Validation"
        );
      }

      if (
        candidate.applications !== undefined &&
        candidate.applications !== null &&
        (typeof candidate.applications !== "object" || Array.isArray(candidate.applications))
      ) {
        return fail(
          `manifest: '${manifestPath}' failed validation: 'applications' must be an object`,
          "Validation"
        );
      }

      return ok(this.normalize(candidate as ConfigurationManifest));
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err);
      return fail(`manifest: '${manifestPath}' failed validation: ${message}`, "Validation");
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
   * Reads the configuration section for one application, validating timeout and private-file rules.
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
    if (section.lockTimeout !== undefined && section.lockTimeout !== null) {
      const parseRes = TimeSpanParser.parse(section.lockTimeout);
      if (!parseRes.ok) {
        return fail(
          `manifest: '${manifestPath}' names lockTimeout '${section.lockTimeout}' for '${applicationName}', which is not a positive TimeSpan`,
          "Validation"
        );
      }
      lockTimeoutMs = parseRes.value;
    }

    const secretsMap = section.keyVault?.secrets ?? {};
    if (Object.keys(secretsMap).length > 0) {
      const privateSafe = DirectorySafety.verifyPrivateFile(manifestPath);
      if (!privateSafe.ok) {
        return privateSafe;
      }
    }

    const resolvedDir = this.resolveDirectory(manifestPath, section.configurationDirectory);

    return ok({
      configurationDirectory: resolvedDir,
      lockTimeoutMs,
      ...(section.keyVault !== undefined ? { keyVault: section.keyVault } : {})
    });
  }

  private static normalize(manifest: ConfigurationManifest): ConfigurationManifest {
    const normalizedApps: Record<string, ApplicationManifestEntry> = {};
    const rawApps = manifest.applications ?? {};

    for (const [name, section] of Object.entries(rawApps)) {
      if (!section || typeof section !== "object") {
        continue;
      }
      if (!section.keyVault) {
        normalizedApps[name] = section;
      } else {
        normalizedApps[name] = {
          ...section,
          keyVault: {
            ...section.keyVault,
            settings: section.keyVault.settings ?? {},
            secrets: section.keyVault.secrets ?? {}
          }
        };
      }
    }

    return {
      version: manifest.version,
      applications: normalizedApps
    };
  }

  /**
   * Strips // line comments, /* block comments * /, and trailing commas outside string literals.
   */
  private static stripJsonCommentsAndTrailingCommas(input: string): string {
    let out = "";
    let inString = false;
    let escaped = false;
    let i = 0;

    while (i < input.length) {
      const ch = input[i]!;
      const next = input[i + 1];

      if (inString) {
        out += ch;
        if (escaped) {
          escaped = false;
        } else if (ch === "\\") {
          escaped = true;
        } else if (ch === "\"") {
          inString = false;
        }
        i++;
        continue;
      }

      if (ch === "\"") {
        inString = true;
        out += ch;
        i++;
        continue;
      }

      if (ch === "/" && next === "/") {
        i += 2;
        while (i < input.length && input[i] !== "\n") {
          i++;
        }
        continue;
      }

      if (ch === "/" && next === "*") {
        i += 2;
        while (i < input.length && !(input[i] === "*" && input[i + 1] === "/")) {
          i++;
        }
        i += 2;
        continue;
      }

      out += ch;
      i++;
    }

    // Remove trailing commas outside string literals before } or ]
    let finalOut = "";
    inString = false;
    escaped = false;
    for (let j = 0; j < out.length; j++) {
      const ch = out[j]!;
      if (inString) {
        finalOut += ch;
        if (escaped) {
          escaped = false;
        } else if (ch === "\\") {
          escaped = true;
        } else if (ch === "\"") {
          inString = false;
        }
        continue;
      }

      if (ch === "\"") {
        inString = true;
        finalOut += ch;
        continue;
      }

      if (ch === ",") {
        let k = j + 1;
        while (k < out.length && /\s/.test(out[k]!)) {
          k++;
        }
        if (k < out.length && (out[k] === "}" || out[k] === "]")) {
          continue;
        }
      }

      finalOut += ch;
    }

    return finalOut;
  }
}
