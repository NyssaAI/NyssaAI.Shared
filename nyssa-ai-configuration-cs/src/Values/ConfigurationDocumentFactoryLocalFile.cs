using NyssaAI.Results;

using NyssaAI.Configuration.FileSecurity;
using NyssaAI.Configuration.Contracts;

namespace NyssaAI.Configuration.Values;

/// <summary>
/// ConfigurationDocumentFactoryLocalFile opens one JSON file per namespace inside a single directory,
/// rejecting any namespace that is not a portable single file name.
/// </summary>
/// <remarks>
/// A namespace becomes <c>&lt;namespace&gt;.json</c>, so it is validated as a file name -- but
/// against a fixed allow-list rather than the current platform's invalid-character set, so a
/// namespace accepted on Linux is not refused on Windows for the same shared directory. Letters,
/// digits, <c>.</c>, <c>_</c> and <c>-</c> are allowed; the name may not start with a dot, and
/// Windows reserved device names (<c>CON</c>, <c>NUL</c>, <c>COM1</c>, ...) are refused on every
/// platform.
/// </remarks>
internal sealed class ConfigurationDocumentFactoryLocalFile : IConfigurationDocumentFactory
{
    /// <summary>The namespace name reserved for the manifest, which no store may write.</summary>
    public const string RegistryNamespace = "config-manifest";

    private static readonly HashSet<string> WindowsReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    private readonly string _directory;
    private readonly TimeSpan? _lockTimeout;

    /// <summary>Creates a factory over one configuration directory.</summary>
    /// <param name="directory">The directory holding the namespace documents.</param>
    /// <param name="lockTimeout">
    /// How long a document waits for its cross-process lock before failing; the document's own
    /// default when null. A non-positive value is reported by <see cref="Create"/>.
    /// </param>
    public ConfigurationDocumentFactoryLocalFile(string directory, TimeSpan? lockTimeout = null)
    {
        _directory = Path.GetFullPath(directory);
        _lockTimeout = lockTimeout;
    }

    /// <inheritdoc/>
    public Result<IConfigurationDocument> Create(string @namespace)
    {
        if (!IsPortableFileName(@namespace))
            return Result<IConfigurationDocument>.Failure(NamespaceNotAFileName(@namespace));

        // The manifest lives at ~/.nyssa-ai/config-manifest.json. A store whose directory is
        // ~/.nyssa-ai writing this namespace would replace the manifest with an entries document
        // and destroy every application's section.
        var path = Path.Combine(_directory, $"{@namespace}.json");
        if (IsRegistryNamespace(@namespace) || IsRegistryPath(path))
            return Result<IConfigurationDocument>.Failure(NamespaceReserved(@namespace));

        // Checked here rather than at resolution, because this is the point where the directory
        // stops being a candidate and starts being read from and written to. A directory another
        // account can write to lets that account swap a namespace document for one of its own,
        // and every value the reader gets back is well-formed and correctly sealed.
        var safe = DirectorySafety.Verify(_directory);
        if (safe.IsFailure)
            return Result<IConfigurationDocument>.Failure(safe.Errors);

        var document = ConfigurationDocumentLocalFile.Create(path, _lockTimeout);
        return document.IsFailure
            ? Result<IConfigurationDocument>.Failure(document.Errors)
            : Result<IConfigurationDocument>.Success(document.Value);
    }

    /// <summary>The failure returned when a namespace cannot be used as a portable file name.</summary>
    /// <param name="namespace">The namespace that was rejected.</param>
    public static Error NamespaceNotAFileName(string @namespace) => Error.FromMessages(
        "The configuration namespace is not valid.",
        $"Namespace '{@namespace}' must be letters, digits, '.', '_' or '-', not start with '.', and not name a reserved device.",
        ErrorCategory.Validation);

    /// <summary>The failure returned when a namespace would address the roots registry.</summary>
    /// <param name="namespace">The namespace that was rejected.</param>
    public static Error NamespaceReserved(string @namespace) => Error.FromMessages(
        "The configuration namespace is reserved.",
        $"Namespace '{@namespace}' is the configuration manifest, which a store may not write.",
        ErrorCategory.Validation);

    private static bool IsPortableFileName(string @namespace)
    {
        if (string.IsNullOrEmpty(@namespace) || @namespace[0] == '.')
            return false;

        foreach (var character in @namespace)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not '.' and not '_' and not '-')
                return false;
        }

        var stem = @namespace.Split('.', 2)[0];
        return !WindowsReservedNames.Contains(stem);
    }

    private static bool IsRegistryNamespace(string @namespace) =>
        string.Equals(@namespace, RegistryNamespace, StringComparison.OrdinalIgnoreCase)
        || string.Equals(@namespace, RegistryNamespace + ".json", StringComparison.OrdinalIgnoreCase);

    private static bool IsRegistryPath(string path)
    {
        var registry = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".nyssa-ai",
            RegistryNamespace + ".json");
        return string.Equals(
            Path.GetFullPath(path),
            Path.GetFullPath(registry),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }
}
