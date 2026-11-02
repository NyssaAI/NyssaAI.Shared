using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using NyssaAI.Configuration.Contracts;
using NyssaAI.Configuration.OsKeyring;
using NyssaAI.Configuration.Tests.Support;
using NyssaAI.Results;

namespace NyssaAI.Configuration.Tests.OsKeyring;

/// <summary>
/// Deterministic unit tests for <see cref="KeyringLinux"/> and <see cref="KeyringSystemdCreds"/>,
/// plus opt-in live tests against <c>systemd-creds</c> on Linux (see <see cref="RealKeyring"/>).
/// </summary>
public sealed partial class KeyringLinuxTests : KeyringStoreTests
{
    private static readonly Lazy<bool> SystemdCredsInstalled = new(() =>
    {
        try
        {
            using var probe = Start("systemd-creds", ["--version"]);
            probe.WaitForExit();
            return probe.ExitCode == 0;
        }
        catch (Exception exception) when (exception is Win32Exception or FileNotFoundException)
        {
            return false;
        }
    });

    /// <inheritdoc/>
    protected override IKeyring Create() => new KeyringLinux();

    /// <inheritdoc/>
    protected override void RequirePlatform()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Uses systemd-creds on Linux.");
        Assert.SkipUnless(SystemdCredsInstalled.Value,
            "systemd-creds is required for live interoperability checks.");
    }

    [Fact]
    public void Set_FilesTheSecretWhereSystemdCredsFindsIt()
    {
        var keyring = Ready();
        var key = NewKey();
        const string Secret = "stored by the adapter";

        Assert.True(keyring.Set(RealKeyring.Service, key, Secret).IsSuccess);

        var credName = KeyringLinux.GetCredentialName(RealKeyring.Service, key);
        var storeDir = Environment.GetEnvironmentVariable(KeyringLinux.CredsDirEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(storeDir))
        {
            storeDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".nyssa-ai",
                "credentials");
        }

        var credFile = Path.Combine(storeDir, $"{credName}.cred");
        Assert.True(File.Exists(credFile));

        string[] args = IsLinuxNonRoot()
            ? ["decrypt", $"--name={credName}", "--user", credFile, "-"]
            : ["decrypt", $"--name={credName}", credFile, "-"];
        var (exitCode, output) = Run("systemd-creds", args);
        Assert.Equal(0, exitCode);
        Assert.Equal(Secret, output.TrimEnd('\r', '\n'));
    }

    [Fact]
    public void Get_ReadsASecretSystemdCredsStoredAtTheSameAddress()
    {
        var keyring = Ready();
        var key = NewKey();
        const string Secret = "placed by an operator";

        var credName = KeyringLinux.GetCredentialName(RealKeyring.Service, key);
        var storeDir = Environment.GetEnvironmentVariable(KeyringLinux.CredsDirEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(storeDir))
        {
            storeDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".nyssa-ai",
                "credentials");
        }

        Directory.CreateDirectory(storeDir);
        var credFile = Path.Combine(storeDir, $"{credName}.cred");
        string[] args = IsLinuxNonRoot()
            ? ["encrypt", $"--name={credName}", "--user", "-", credFile]
            : ["encrypt", $"--name={credName}", "-", credFile];

        var stored = Run("systemd-creds", args, Secret);
        Assert.Equal(0, stored.ExitCode);

        Assert.Equal(Secret, keyring.Get(RealKeyring.Service, key).Value);
    }

    [Fact]
    public void Set_ThenGet_ThenDelete_WithInjectedRunner_RoundTripsThroughCredentialFile()
    {
        using var tempDir = new TemporaryDirectory();
        var recordedCalls = new List<(string Binary, string[] Args, string? Input)>();

        var keyring = new KeyringLinux(
            (binary, args, input) =>
            {
                recordedCalls.Add((binary, args, input));
                if (args[0] == "encrypt")
                {
                    var outPath = args[^1];
                    File.WriteAllText(outPath, $"ENCRYPTED:{input}");
                    return (0, string.Empty, string.Empty);
                }

                if (args[0] == "decrypt")
                {
                    var inPath = args[^2];
                    var payload = File.ReadAllText(inPath);
                    return (0, payload["ENCRYPTED:".Length..] + "\n", string.Empty);
                }

                return (1, string.Empty, "unexpected command");
            },
            tempDir.Path);

        // Missing file returns null without invoking systemd-creds
        var initial = keyring.Get("nyssa-ai-configuration", "AcmeApp");
        Assert.True(initial.IsSuccess);
        Assert.Null(initial.Value);
        Assert.Empty(recordedCalls);

        // Set encrypts and writes <credName>.cred
        var setResult = keyring.Set("nyssa-ai-configuration", "AcmeApp", "super-secret-value");
        Assert.True(setResult.IsSuccess);
        var credFile = Path.Combine(tempDir.Path, "nyssa-ai-configuration.AcmeApp.cred");
        Assert.True(File.Exists(credFile));

        // Get decrypts from <credName>.cred
        var getResult = keyring.Get("nyssa-ai-configuration", "AcmeApp");
        Assert.True(getResult.IsSuccess);
        Assert.Equal("super-secret-value", getResult.Value);

        // Delete removes <credName>.cred
        var deleteResult = keyring.Delete("nyssa-ai-configuration", "AcmeApp");
        Assert.True(deleteResult.IsSuccess);
        Assert.False(File.Exists(credFile));
        Assert.Null(keyring.Get("nyssa-ai-configuration", "AcmeApp").Value);
    }

    [Fact]
    public void Get_AndSet_WhenSystemdCredsFails_ReturnUnavailableErrors()
    {
        using var tempDir = new TemporaryDirectory();
        var credFile = Path.Combine(tempDir.Path, "svc.key1.cred");
        File.WriteAllText(credFile, "corrupted-cred-blob");

        var failingKeyring = new KeyringLinux(
            (_, _, _) => (1, string.Empty, "Failed to decrypt credential."),
            tempDir.Path);

        var readFailure = failingKeyring.Get("svc", "key1");
        Assert.True(readFailure.IsFailure);
        Assert.Equal(ErrorCategory.Unavailable, readFailure.Errors[0].Category);
        Assert.Contains("Failed to decrypt credential.", readFailure.Errors[0].TechnicalMessage, StringComparison.Ordinal);

        var writeFailure = failingKeyring.Set("svc", "key2", "secret");
        Assert.True(writeFailure.IsFailure);
        Assert.Equal(ErrorCategory.Unavailable, writeFailure.Errors[0].Category);
        Assert.False(File.Exists(Path.Combine(tempDir.Path, "svc.key2.cred")));

        var missingBinaryKeyring = new KeyringLinux(
            (_, _, _) => throw new Win32Exception("No such file or directory"),
            tempDir.Path);

        var launchFailure = missingBinaryKeyring.Get("svc", "key1");
        Assert.True(launchFailure.IsFailure);
        Assert.Equal(ErrorCategory.Unavailable, launchFailure.Errors[0].Category);
        Assert.Contains("Win32Exception", launchFailure.Errors[0].TechnicalMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void KeyringSystemdCreds_AndKeyringLinux_ReadFromCredentialsDirectoryWhenPresent()
    {
        using var credsDir = new TemporaryDirectory();
        using var storeDir = new TemporaryDirectory();
        var previous = Environment.GetEnvironmentVariable(KeyringSystemdCreds.CredentialsDirectoryVariable);
        try
        {
            Environment.SetEnvironmentVariable(KeyringSystemdCreds.CredentialsDirectoryVariable, credsDir.Path);
            File.WriteAllText(Path.Combine(credsDir.Path, "nyssa-ai-configuration.MyApp"), "  from-systemd-creds-dir \n");
            File.WriteAllText(Path.Combine(credsDir.Path, "nyssa-ai-configuration.OtherApp"), "other-secret");

            Assert.True(KeyringSystemdCreds.IsAvailable());
            var defaultKeyring = KeyringDefault.Create();
            Assert.IsType<KeyringSystemdCreds>(defaultKeyring);

            var systemdCreds = new KeyringSystemdCreds();
            var readDirect = systemdCreds.Get("nyssa-ai-configuration", "MyApp");
            Assert.True(readDirect.IsSuccess);
            Assert.Equal("from-systemd-creds-dir", readDirect.Value);

            var missing = systemdCreds.Get("other-service", "missing-key");
            Assert.True(missing.IsFailure);
            Assert.Equal(ErrorCategory.NotFound, missing.Errors[0].Category);

            var setAttempt = systemdCreds.Set("nyssa-ai-configuration", "MyApp", "new-secret");
            Assert.True(setAttempt.IsFailure);
            Assert.Contains("systemd-creds is immutable at runtime", setAttempt.Errors[0].UserMessage, StringComparison.Ordinal);

            var deleteAttempt = systemdCreds.Delete("nyssa-ai-configuration", "MyApp");
            Assert.True(deleteAttempt.IsFailure);
            Assert.Contains("systemd-creds is immutable at runtime", deleteAttempt.Errors[0].UserMessage, StringComparison.Ordinal);

            var linuxKeyring = new KeyringLinux(
                (_, _, _) => throw new InvalidOperationException("Should not invoke systemd-creds when CREDENTIALS_DIRECTORY matches"),
                storeDir.Path);
            var readViaLinux = linuxKeyring.Get("nyssa-ai-configuration", "MyApp");
            Assert.True(readViaLinux.IsSuccess);
            Assert.Equal("from-systemd-creds-dir", readViaLinux.Value);
        }
        finally
        {
            Environment.SetEnvironmentVariable(KeyringSystemdCreds.CredentialsDirectoryVariable, previous);
        }
    }

    private static bool IsLinuxNonRoot() =>
        !OperatingSystem.IsLinux() || GetEffectiveUid() != 0;

    private static (int ExitCode, string Output) Run(string fileName, string[] arguments, string? input = null)
    {
        using var process = Start(fileName, arguments, input is not null);
        if (input is not null)
        {
            process.StandardInput.Write(input);
            process.StandardInput.Close();
        }

        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output);
    }

    private static Process Start(string fileName, string[] arguments, bool redirectInput = false)
    {
        var start = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = redirectInput,
            UseShellExecute = false,
        };

        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);

        return Process.Start(start) ?? throw new InvalidOperationException($"'{fileName}' did not start.");
    }

    [LibraryImport("libc", EntryPoint = "geteuid")]
    private static partial uint GetEffectiveUid();
}
