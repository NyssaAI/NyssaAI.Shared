using NyssaAI.Results;

namespace NyssaAI.Configuration.Manifest;

/// <summary>
/// Validates <c>namespaceRegistry</c> entries and resolves per-application <c>sharedNamespaces</c>
/// selections relative to the manifest file directory.
/// </summary>
internal static class ManifestNamespaceRegistry
{
    private static readonly HashSet<string> ReservedNamespaceStems = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>Validates all <c>namespaceRegistry</c> entries and returns normalized relative/absolute path values.</summary>
    internal static Result<Dictionary<string, string>> Parse(
        string manifestPath,
        IReadOnlyDictionary<string, string>? rawRegistry)
    {
        var namespaceRegistry = new Dictionary<string, string>(StringComparer.Ordinal);
        if (rawRegistry is null)
            return Result<Dictionary<string, string>>.Success(namespaceRegistry);

        foreach (var (namespaceName, filePath) in rawRegistry)
        {
            if (!IsValidNamespaceName(namespaceName))
            {
                return Result<Dictionary<string, string>>.Failure(ConfigurationManifestReader.ValidationFailure(
                    $"manifest: namespaceRegistry.{namespaceName} has an invalid namespace identifier"));
            }

            if (string.IsNullOrWhiteSpace(filePath) || filePath.Any(char.IsControl))
            {
                return Result<Dictionary<string, string>>.Failure(ConfigurationManifestReader.ValidationFailure(
                    $"manifest: namespaceRegistry.{namespaceName} must name a non-blank file path without control characters"));
            }

            var resolvedNamespacePath = ConfigurationManifestReader.ResolveDirectory(manifestPath, filePath);
            var resolvedManifestPath = Path.GetFullPath(manifestPath);
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (string.Equals(resolvedNamespacePath, resolvedManifestPath, comparison))
            {
                return Result<Dictionary<string, string>>.Failure(ConfigurationManifestReader.ValidationFailure(
                    $"manifest: namespaceRegistry.{namespaceName} must not point to config-manifest.json"));
            }

            namespaceRegistry[namespaceName] = filePath;
        }

        return Result<Dictionary<string, string>>.Success(namespaceRegistry);
    }

    /// <summary>Validates that an application's <c>sharedNamespaces</c> entries are valid and registered.</summary>
    internal static Result ValidateSharedNamespaces(
        string appName,
        string[]? sharedNamespaces,
        IReadOnlyDictionary<string, string> registry)
    {
        for (var index = 0; index < (sharedNamespaces?.Length ?? 0); index++)
        {
            var namespaceName = sharedNamespaces![index];
            if (!IsValidNamespaceName(namespaceName))
            {
                return Result.Failure(ConfigurationManifestReader.ValidationFailure(
                    $"manifest: applications.{appName}.sharedNamespaces[{index}] has an invalid namespace identifier"));
            }

            if (!registry.ContainsKey(namespaceName))
            {
                return Result.Failure(ConfigurationManifestReader.ValidationFailure(
                    $"manifest: applications.{appName}.sharedNamespaces[{index}] refers to unknown namespaceRegistry entry '{namespaceName}'"));
            }
        }

        return Result.Success();
    }

    /// <summary>Resolves each selected namespace in <paramref name="sharedNamespaces"/> relative to <paramref name="manifestPath"/>.</summary>
    internal static Dictionary<string, string> ResolveApplicationFiles(
        string manifestPath,
        string[]? sharedNamespaces,
        IReadOnlyDictionary<string, string> registry)
    {
        var namespaceFiles = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var namespaceName in sharedNamespaces ?? [])
            namespaceFiles[namespaceName] = ConfigurationManifestReader.ResolveDirectory(manifestPath, registry[namespaceName]);

        return namespaceFiles;
    }

    /// <summary>Returns whether a namespace identifier is safe as a portable document filename.</summary>
    internal static bool IsValidNamespaceName(string? namespaceName)
    {
        if (string.IsNullOrWhiteSpace(namespaceName)
            || !string.Equals(namespaceName, namespaceName.Trim(), StringComparison.Ordinal)
            || namespaceName[0] == '.'
            || namespaceName.EndsWith(".", StringComparison.Ordinal)
            || namespaceName.Contains("..", StringComparison.Ordinal)
            || string.Equals(namespaceName, "config-manifest", StringComparison.OrdinalIgnoreCase)
            || string.Equals(namespaceName, "config-manifest.json", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        foreach (var character in namespaceName)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not '.' and not '_' and not '-')
                return false;
        }

        var stem = namespaceName.Split('.', 2)[0];
        return !ReservedNamespaceStems.Contains(stem);
    }
}
