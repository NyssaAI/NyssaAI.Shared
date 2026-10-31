using System.Diagnostics;
using System.Runtime.Versioning;
using NyssaAI.Configuration.Contracts;
using NyssaAI.Configuration.OsKeyring;

namespace NyssaAI.Configuration.Tests.OsKeyring;

/// <summary>
/// <see cref="KeyringLinux"/> against the Secret Service of whoever runs it. Manual and opt-in:
/// see <see cref="RealKeyring"/>.
/// </summary>
/// <remarks>
/// <para>
/// This needs an unlocked login keyring behind a session bus -- a desktop session, or a headless
/// one primed with <c>dbus-run-session</c> and <c>gnome-keyring-daemon --unlock</c>. It also needs
/// <c>secret-tool</c> (Debian and Ubuntu package <c>libsecret-tools</c>) to check the addressing
/// independently of the adapter. Without it the interop checks skip. Cleanup uses <see cref="IKeyring.Delete"/>.
/// </para>
/// </remarks>
[SupportedOSPlatform("linux")]
public sealed class KeyringLinuxTests : KeyringStoreTests
{
    private static readonly Lazy<bool> SecretToolInstalled = new(() =>
    {
        try
        {
            using var probe = Start("secret-tool", ["--help"]);
            probe.WaitForExit();
            return true;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            return false;
        }
    });

    /// <inheritdoc/>
    protected override IKeyring Create() => new KeyringLinux();

    /// <inheritdoc/>
    protected override void RequirePlatform()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Reads the Secret Service through libsecret.");
        Assert.SkipUnless(SecretToolInstalled.Value,
            "secret-tool is required for interoperability checks; install libsecret-tools.");
    }

    [Fact]
    public void Set_FilesTheSecretWhereSecretToolFindsIt()
    {
        var keyring = Ready();
        var key = NewKey();
        const string Secret = "stored by the adapter";

        Assert.True(keyring.Set(RealKeyring.Service, key, Secret).IsSuccess);

        // The addressing the class documents: attributes service=S and username=K under the generic
        // schema, which is what secret-tool and the Python keyring package use. An adapter that
        // agreed with itself on some other attribute pair would pass every round-trip test in the
        // base class while being invisible to the tools the docs point operators at.
        var (exitCode, output) = Run("secret-tool", ["lookup", "service", RealKeyring.Service, "username", key]);
        Assert.Equal(0, exitCode);
        Assert.Equal(Secret, output.TrimEnd('\n'));
    }

    [Fact]
    public void Get_ReadsASecretSecretToolStoredAtTheSameAddress()
    {
        var keyring = Ready();
        var key = NewKey();
        const string Secret = "placed by an operator";

        var stored = Run(
            "secret-tool",
            ["store", "--label=nyssa configuration test", "service", RealKeyring.Service, "username", key],
            Secret);
        Assert.Equal(0, stored.ExitCode);

        // The other direction of the same contract: the secret-tool invocation the class doc tells
        // an operator to run has to produce something this adapter can find.
        Assert.Equal(Secret, keyring.Get(RealKeyring.Service, key).Value);
    }

    [Fact]
    public void Get_WhenLibsecretIsMissing_IsAFailureAndNotAnAbsentSecret()
    {
        RequirePlatform();
        RealKeyring.RequireOptIn();

        // Not reachable in-process -- the libraries are loaded by name from the system path, so the
        // only honest check here is that the error the loader path produces is a failure rather
        // than a success carrying null. A machine without libsecret answers this on its own; one
        // with it skips, because forcing the loader to fail would mean unloading a library the rest
        // of the run needs.
        var found = new KeyringLinux().Get(RealKeyring.Service, NewKey());

        Assert.SkipWhen(found.IsSuccess, "libsecret is installed, so the loader failure cannot be observed.");
        Assert.Contains(
            found.Errors,
            error => error.TechnicalMessage.Contains("DllNotFoundException", StringComparison.Ordinal)
                || error.TechnicalMessage.Contains("EntryPointNotFoundException", StringComparison.Ordinal));
    }

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
}
