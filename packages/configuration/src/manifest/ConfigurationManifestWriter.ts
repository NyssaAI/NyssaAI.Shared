// Responsibility: Write and provision application sections in the configuration manifest atomically.
// Public interface:
// +-- ConfigurationManifestWriter
// |   `-- setConfigurationDirectory(manifestPath, applicationName, configurationDirectory, lockTimeoutMs)

import * as fs from "node:fs";
import * as path from "node:path";
import * as crypto from "node:crypto";
import { ok, fail, type Result } from "../results/Result.js";
import { ProtectedFileSystem } from "../fileSecurity/ProtectedFileSystem.js";
import { ConfigurationManifestReader } from "./ConfigurationManifestReader.js";
import { TimeSpanParser } from "./TimeSpanParser.js";
import type { ConfigurationManifest, ApplicationManifestEntry } from "./ConfigurationManifest.js";

/**
 * ConfigurationManifestWriter updates configuration manifest sections atomically.
 */
export class ConfigurationManifestWriter {
  /**
   * Sets or creates the configuration directory for an application in the manifest.
   */
  public static setConfigurationDirectory(
    manifestPath: string,
    applicationName: string,
    configurationDirectory: string,
    lockTimeoutMs?: number
  ): Result<string> {
    const resolvedPath = path.resolve(manifestPath);
    const parentDir = path.dirname(resolvedPath);

    const dirRes = ProtectedFileSystem.createDirectory(parentDir);
    if (!dirRes.ok) {
      return dirRes;
    }

    let manifest: ConfigurationManifest = {
      version: 1,
      applications: {}
    };

    if (fs.existsSync(resolvedPath)) {
      const readRes = ConfigurationManifestReader.readManifest(resolvedPath);
      if (readRes.ok) {
        manifest = readRes.value;
      }
    }

    const apps: Record<string, ApplicationManifestEntry> = {
      ...(manifest.applications || {})
    };

    const existingSection = apps[applicationName] || {};
    const updatedSection: ApplicationManifestEntry = {
      ...existingSection,
      configurationDirectory,
      lockTimeout: lockTimeoutMs !== undefined ? TimeSpanParser.format(lockTimeoutMs) : existingSection.lockTimeout
    };

    apps[applicationName] = updatedSection;

    const updatedManifest: ConfigurationManifest = {
      version: manifest.version || 1,
      applications: apps
    };

    // Atomic write via unique temp file
    const tempFile = `${resolvedPath}.${crypto.randomUUID()}.tmp`;
    try {
      const json = JSON.stringify(updatedManifest, null, 2);
      ProtectedFileSystem.createFile(tempFile, json);
      fs.renameSync(tempFile, resolvedPath);
      return ok(configurationDirectory);
    } catch (err) {
      try {
        if (fs.existsSync(tempFile)) fs.unlinkSync(tempFile);
      } catch {
        // ignore
      }
      const message = err instanceof Error ? err.message : String(err);
      return fail(`Failed to write configuration manifest: ${message}`);
    }
  }
}
