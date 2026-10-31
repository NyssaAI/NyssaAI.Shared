using System.ComponentModel;
using System.Diagnostics;
using NyssaAI.Results;

using NyssaAI.Configuration.Contracts;

namespace NyssaAI.Configuration.OAuth.Infrastructure;

/// <summary>
/// BrowserLauncherShell is the default <see cref="IBrowserLauncher"/>: it hands the URL to the
/// platform shell, which opens the default browser, falling back to <c>xdg-open</c> on Linux.
/// </summary>
/// <remarks>
/// The URL reaches a shell-executing launch, so it must already be a well-formed absolute
/// <see cref="Uri"/> with every query value escaped; the authorization client builds it that way.
/// </remarks>
internal sealed class BrowserLauncherShell : IBrowserLauncher
{
    /// <inheritdoc/>
    public ValueTask<Result> LaunchAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        if (!uri.IsAbsoluteUri || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return ValueTask.FromResult(Result.Failure(UrlNotHttps(uri)));

        // This is the adapter boundary: Process.Start throws when no handler is registered.
        try
        {
            using var process = OperatingSystem.IsLinux()
                ? Process.Start(new ProcessStartInfo("xdg-open", uri.AbsoluteUri) { UseShellExecute = false })
                : Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            return ValueTask.FromResult(Result.Success());
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException
            or PlatformNotSupportedException or IOException)
        {
            return ValueTask.FromResult(Result.Failure(LaunchFailed(exception)));
        }
    }

    /// <summary>The failure returned when the URL is not an absolute <c>https</c> URI.</summary>
    /// <param name="uri">The URL that was refused.</param>
    public static Error UrlNotHttps(Uri uri) => Error.FromMessages(
        "The sign-in page could not be opened.",
        $"oauth: refusing to open '{uri}': only absolute https URLs are launched",
        ErrorCategory.Validation);

    /// <summary>The failure returned when the shell could not open the browser.</summary>
    /// <param name="exception">The underlying launch failure.</param>
    public static Error LaunchFailed(Exception exception) => Error.FromMessages(
        "The sign-in page could not be opened in your browser.",
        $"oauth: browser launch failed: {exception.GetType().Name}: {exception.Message}",
        ErrorCategory.Unexpected);
}
