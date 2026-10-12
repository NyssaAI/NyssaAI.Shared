using NyssaAI.Results;

namespace NyssaAI.Configuration.Contracts;

/// <summary>
/// IBrowserLauncher opens the authorization URL for the user. Injecting it is what makes the
/// listener, PKCE, and state validation testable without a browser or a human: a test's launcher
/// plays the user by requesting the redirect URI itself.
/// </summary>
public interface IBrowserLauncher
{
    /// <summary>Opens a URL in the user's browser. Returns once the launch has been handed off.</summary>
    /// <param name="uri">The absolute <c>https</c> URL to open.</param>
    /// <param name="cancellationToken">Token used to cancel the launch.</param>
    ValueTask<Result> LaunchAsync(Uri uri, CancellationToken cancellationToken = default);
}
