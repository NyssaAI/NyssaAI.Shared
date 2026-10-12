namespace NyssaAI.Configuration.Tests.OsKeyring;

/// <summary>
/// The gate on the tests that talk to the machine's real credential store.
/// </summary>
/// <remarks>
/// These tests write to, and delete from, the credential store of whoever runs them. That is not
/// something a build should decide to do on its own, so they are opt-in and stay skipped until an
/// operator sets the variable by hand. They are not run in CI: a hosted agent has no unlocked
/// keyring to answer them, and a suite that reports "the store is unavailable" as a failure on
/// every run teaches everyone to ignore it.
/// </remarks>
internal static class RealKeyring
{
    /// <summary>The environment variable an operator sets to let these tests run.</summary>
    public const string OptInVariable = "NYSSA_OS_KEYRING_TESTS";

    /// <summary>The service name every credential these tests file is filed under.</summary>
    public const string Service = "nyssa.configuration.tests";

    /// <summary>Ends the test as skipped unless the operator opted in.</summary>
    public static void RequireOptIn() => Assert.SkipUnless(
        Environment.GetEnvironmentVariable(OptInVariable) is "1" or "true",
        $"Set {OptInVariable}=1 to run against this machine's real credential store.");
}
