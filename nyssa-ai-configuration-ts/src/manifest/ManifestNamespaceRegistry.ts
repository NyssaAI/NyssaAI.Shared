// Responsibility: Validate and resolve opt-in shared namespace registry entries from a manifest.
// Public interface:
// `-- ManifestNamespaceRegistry
//     +-- parse(manifestPath, value)
//     `-- resolveApplicationFiles(manifestPath, appName, sharedNamespaces, registry)

import * as path from "node:path";
import { ok, fail, type Result } from "@nyssa-ai/result";

export class ManifestNamespaceRegistry {
  public static parse(manifestPath: string, value: unknown): Result<Record<string, string>> {
    if (value !== undefined && value !== null && (typeof value !== "object" || Array.isArray(value))) {
      return fail(
        `manifest: '${manifestPath}' failed validation: 'namespaceRegistry' must be an object`,
        "Validation"
      );
    }

    const registryEntries: [string, string][] = [];
    for (const [namespaceName, rawPath] of Object.entries((value as Record<string, unknown> | undefined) ?? {})) {
      if (!this.isValidNamespaceName(namespaceName)) {
        return fail(`manifest: namespaceRegistry.${namespaceName} has an invalid namespace identifier`, "Validation");
      }
      if (typeof rawPath !== "string" || rawPath.trim().length === 0 || /[\x00-\x1F\x7F]/.test(rawPath)) {
        return fail(
          `manifest: namespaceRegistry.${namespaceName} must name a non-blank file path without control characters`,
          "Validation"
        );
      }

      const resolvedNamespacePath = this.resolvePath(manifestPath, rawPath);
      const resolvedManifestPath = path.resolve(manifestPath);
      const pathsMatch =
        process.platform === "win32"
          ? resolvedNamespacePath.toLowerCase() === resolvedManifestPath.toLowerCase()
          : resolvedNamespacePath === resolvedManifestPath;
      if (pathsMatch) {
        return fail(`manifest: namespaceRegistry.${namespaceName} must not point to config-manifest.json`, "Validation");
      }
      registryEntries.push([namespaceName, rawPath]);
    }

    return ok(Object.fromEntries(registryEntries));
  }

  public static resolveApplicationFiles(
    manifestPath: string,
    appName: string,
    sharedNamespaces: unknown,
    registry: Readonly<Record<string, string>>
  ): Result<Record<string, string>> {
    if (sharedNamespaces === undefined || sharedNamespaces === null) {
      return ok({});
    }
    if (!Array.isArray(sharedNamespaces)) {
      return fail(
        `manifest: applications.${appName}.sharedNamespaces must be an array of namespace names`,
        "Validation"
      );
    }

    const fileEntries: [string, string][] = [];
    for (const [index, namespaceName] of sharedNamespaces.entries()) {
      if (typeof namespaceName !== "string" || !this.isValidNamespaceName(namespaceName)) {
        return fail(
          `manifest: applications.${appName}.sharedNamespaces[${index}] has an invalid namespace identifier`,
          "Validation"
        );
      }
      if (!Object.prototype.hasOwnProperty.call(registry, namespaceName)) {
        return fail(
          `manifest: applications.${appName}.sharedNamespaces[${index}] refers to unknown namespaceRegistry entry '${namespaceName}'`,
          "Validation"
        );
      }
      fileEntries.push([namespaceName, this.resolvePath(manifestPath, registry[namespaceName]!)]);
    }

    return ok(Object.fromEntries(fileEntries));
  }

  private static isValidNamespaceName(namespaceName: string): boolean {
    return (
      namespaceName.trim().length > 0 &&
      namespaceName === namespaceName.trim() &&
      !namespaceName.startsWith(".") &&
      !namespaceName.endsWith(".") &&
      !namespaceName.includes("..") &&
      /^[A-Za-z0-9._-]+$/.test(namespaceName) &&
      !/^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)/i.test(namespaceName) &&
      !["config-manifest", "config-manifest.json"].includes(namespaceName.toLowerCase())
    );
  }

  private static resolvePath(manifestPath: string, filePath: string): string {
    return path.isAbsolute(filePath)
      ? path.resolve(filePath)
      : path.resolve(path.dirname(manifestPath), filePath);
  }
}
