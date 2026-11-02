using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using NyssaAI.Configuration.Contracts;
using NyssaAI.Results;

namespace NyssaAI.Configuration.OsKeyring;

/// <summary>
/// KeyringLinux manages secrets on Linux using <c>systemd-creds</c>. A secret filed under service
/// <c>S</c> and key <c>K</c> is stored as an encrypted credential file named
/// <c>&lt;Sanitize(S)&gt;.&lt;Sanitize(K)&gt;.cred</c> inside <c>~/.nyssa-ai/credentials</c> (or
/// <c>NYSSA_SYSTEMD_CREDS_DIR</c> when set), encrypted and decrypted via <c>systemd-creds</c>.
/// </summary>
/// <remarks>
/// <para>
/// On reads, if <c>CREDENTIALS_DIRECTORY</c> is set and contains a matching pre-decrypted systemd
/// credential file, its content is returned directly. Otherwise, if the <c>.cred</c> file does not
/// exist, <c>null</c> is returned to signal confirmed absence. When the <c>.cred</c> file exists,
/// <c>systemd-creds decrypt</c> is invoked and any non-zero exit code or process launch failure is
/// reported as <see cref="ErrorCategory.Unavailable"/>, never mistaken for an absent secret.
/// </para>
/// </remarks>
internal sealed partial class KeyringLinux : IKeyring
{
    /// <summary>Environment variable overriding the directory where encrypted <c>.cred</c> files are stored.</summary>
    public const string CredsDirEnvironmentVariable = "NYSSA_SYSTEMD_CREDS_DIR";

    private const string SystemdCredsBinary = "systemd-creds";

    private readonly Func<string, string[], string?, (int ExitCode, string StdOut, string StdErr)> _runner;
    private readonly string? _storeDirectory;

    /// <summary>Creates a Linux keyring backed by <c>systemd-creds</c>.</summary>
    public KeyringLinux()
        : this(null, null)
    {
    }

    /// <summary>
    /// Creates a Linux keyring with an optional process runner and store directory for deterministic testing.
    /// </summary>
    internal KeyringLinux(
        Func<string, string[], string?, (int ExitCode, string StdOut, string StdErr)>? runner = null,
        string? storeDirectory = null)
    {
        _runner = runner ?? RunProcess;
        _storeDirectory = storeDirectory;
    }

    /// <inheritdoc/>
    public Result<string?> Get(string service, string key)
    {
        var target = KeyringAddress.Compose(service, key);
        if (target.IsFailure)
            return Result<string?>.Failure(target.Errors);

        var credName = GetCredentialName(service, key);

        var runtimeCredsDir = Environment.GetEnvironmentVariable(KeyringSystemdCreds.CredentialsDirectoryVariable);
        if (!string.IsNullOrWhiteSpace(runtimeCredsDir) && Directory.Exists(runtimeCredsDir))
        {
            try
            {
                var preprovisioned = KeyringSystemdCreds.TryReadCredentialFile(
                    runtimeCredsDir,
                    explicitName: null,
                    credName,
                    service,
                    key);
                if (preprovisioned is not null)
                    return Result<string?>.Success(preprovisioned);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return Result<string?>.Failure(StoreUnavailable(-1, exception.Message));
            }
        }

        var credFilePath = GetCredentialFilePath(credName);
        if (!File.Exists(credFilePath))
            return Result<string?>.Success(null);

        try
        {
            var arguments = BuildDecryptArguments(credName, credFilePath);
            var (exitCode, stdOut, stdErr) = _runner(SystemdCredsBinary, arguments, null);
            if (exitCode != 0)
                return Result<string?>.Failure(StoreUnavailable(exitCode, stdErr.Trim()));

            return Result<string?>.Success(stdOut.TrimEnd('\r', '\n'));
        }
        catch (Exception exception) when (exception is Win32Exception or FileNotFoundException or InvalidOperationException or IOException)
        {
            return Result<string?>.Failure(LibraryUnavailable(exception));
        }
    }

    /// <inheritdoc/>
    public Result Set(string service, string key, string secret)
    {
        var target = KeyringAddress.Compose(service, key);
        if (target.IsFailure)
            return Result.Failure(target.Errors);

        var credName = GetCredentialName(service, key);
        var storeDir = ResolveStoreDirectory();
        var credFilePath = Path.Combine(storeDir, $"{credName}.cred");
        var tempFilePath = Path.Combine(storeDir, $".{credName}.{Guid.NewGuid():N}.tmp");

        try
        {
            Directory.CreateDirectory(storeDir);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(
                    storeDir,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            var arguments = BuildEncryptArguments(credName, tempFilePath);
            var (exitCode, stdOut, stdErr) = _runner(SystemdCredsBinary, arguments, secret);
            if (exitCode != 0)
            {
                TryDeleteFile(tempFilePath);
                return Result.Failure(StoreNotWritable(exitCode, stdErr.Trim()));
            }

            if (!File.Exists(tempFilePath))
                File.WriteAllText(tempFilePath, stdOut);

            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(
                    tempFilePath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            File.Move(tempFilePath, credFilePath, overwrite: true);
            return Result.Success();
        }
        catch (Exception exception) when (exception is Win32Exception or FileNotFoundException or InvalidOperationException)
        {
            TryDeleteFile(tempFilePath);
            return Result.Failure(LibraryUnavailable(exception));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            TryDeleteFile(tempFilePath);
            return Result.Failure(StoreNotWritable(-1, exception.Message));
        }
    }

    /// <inheritdoc/>
    public Result Delete(string service, string key)
    {
        var target = KeyringAddress.Compose(service, key);
        if (target.IsFailure)
            return Result.Failure(target.Errors);

        var credName = GetCredentialName(service, key);
        var credFilePath = GetCredentialFilePath(credName);

        try
        {
            if (File.Exists(credFilePath))
                File.Delete(credFilePath);

            return Result.Success();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Result.Failure(StoreNotDeletable(-1, exception.Message));
        }
    }

    internal static string GetCredentialName(string service, string key) =>
        $"{SanitizeName(service)}.{SanitizeName(key)}";

    internal static string SanitizeName(string value)
    {
        var chars = value.ToCharArray();
        for (var index = 0; index < chars.Length; index++)
        {
            var ch = chars[index];
            var allowed = (ch >= 'a' && ch <= 'z')
                || (ch >= 'A' && ch <= 'Z')
                || (ch >= '0' && ch <= '9')
                || ch is '.' or '_' or '-';
            if (!allowed)
                chars[index] = '_';
        }

        return new string(chars);
    }

    /// <summary>The failure returned when the credential file cannot be deleted.</summary>
    /// <param name="code">The exit or error code.</param>
    /// <param name="message">The diagnostic message.</param>
    public static Error StoreNotDeletable(int code, string? message) => Error.FromMessages(
        "The credential could not be deleted.",
        $"keyring: systemd-creds deletion error {code}: {message}",
        ErrorCategory.Unavailable);

    /// <summary>The failure returned when systemd-creds could not decrypt a credential.</summary>
    /// <param name="code">The exit code.</param>
    /// <param name="message">The diagnostic message from systemd-creds, never a secret.</param>
    public static Error StoreUnavailable(int code, string? message) => Error.FromMessages(
        "The credential store could not be read.",
        $"keyring: systemd-creds reported error {code}: {message}",
        ErrorCategory.Unavailable);

    /// <summary>The failure returned when systemd-creds is not installed or cannot be launched.</summary>
    /// <param name="exception">The process launch failure.</param>
    public static Error LibraryUnavailable(Exception exception) => Error.FromMessages(
        "The credential store is not available on this machine.",
        $"keyring: {exception.GetType().Name}: {exception.Message}",
        ErrorCategory.Unavailable);

    /// <summary>The failure returned when systemd-creds refused to encrypt or store an item.</summary>
    /// <param name="code">The exit code.</param>
    /// <param name="message">The diagnostic message from systemd-creds, never a secret.</param>
    public static Error StoreNotWritable(int code, string? message) => Error.FromMessages(
        "The credential store could not be written.",
        $"keyring: systemd-creds reported error {code}: {message}",
        ErrorCategory.Unavailable);

    private string ResolveStoreDirectory()
    {
        if (!string.IsNullOrWhiteSpace(_storeDirectory))
            return _storeDirectory;

        var configured = Environment.GetEnvironmentVariable(CredsDirEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".nyssa-ai",
            "credentials");
    }

    private string GetCredentialFilePath(string credName) =>
        Path.Combine(ResolveStoreDirectory(), $"{credName}.cred");

    private static string[] BuildDecryptArguments(string credName, string credFilePath) =>
        IsUserMode()
            ? ["decrypt", $"--name={credName}", "--user", credFilePath, "-"]
            : ["decrypt", $"--name={credName}", credFilePath, "-"];

    private static string[] BuildEncryptArguments(string credName, string outputFilePath) =>
        IsUserMode()
            ? ["encrypt", $"--name={credName}", "--user", "-", outputFilePath]
            : ["encrypt", $"--name={credName}", "-", outputFilePath];

    private static bool IsUserMode() =>
        !OperatingSystem.IsLinux() || GetEffectiveUid() != 0;

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Best-effort cleanup of temporary file.
        }
    }

    private static (int ExitCode, string StdOut, string StdErr) RunProcess(
        string fileName,
        string[] arguments,
        string? standardInput)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            RedirectStandardInput = standardInput is not null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start '{fileName}'.");

        if (standardInput is not null)
        {
            process.StandardInput.Write(standardInput);
            process.StandardInput.Close();
        }

        var stdOutTask = process.StandardOutput.ReadToEndAsync();
        var stdErrTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return (process.ExitCode, stdOutTask.GetAwaiter().GetResult(), stdErrTask.GetAwaiter().GetResult());
    }

    [LibraryImport("libc", EntryPoint = "geteuid")]
    private static partial uint GetEffectiveUid();
}
