using System.Net.Sockets;
using System.Text;
using NyssaAI.Configuration.Contracts;
using NyssaAI.Results;

namespace NyssaAI.Configuration.UseCaseTests.fixtures;

/// <summary>
/// Stands in for the user and their browser. Given the authorize URI the library built, it does
/// what a real redirect does: calls the library's own loopback listener with a code and the state
/// it was given. The listener, the state check, and the code exchange are all the real thing.
/// </summary>
public sealed class FakeBrowser : IBrowserLauncher
{
    public const string AuthorizationCode = "code-from-the-fake-browser";

    /// <summary>How many times a browser was opened — the tutorials assert this is zero on the Get path.</summary>
    public int Launches { get; private set; }

    /// <summary>When true, the user clicks "deny" and the redirect carries an error instead of a code.</summary>
    public bool DenyConsent { get; set; }

    public async ValueTask<Result> LaunchAsync(
        Uri uri,
        CancellationToken cancellationToken = default
    )
    {
        Launches++;
        var query = Parse(uri.Query);
        var redirect = new Uri(query["redirect_uri"]);
        var state = query["state"];
        var response = DenyConsent
            ? $"error=access_denied&error_description={Uri.EscapeDataString("The user said no.")}&state={Uri.EscapeDataString(state)}"
            : $"code={Uri.EscapeDataString(AuthorizationCode)}&state={Uri.EscapeDataString(state)}";
        // Don't block the launch: the library waits for the callback after LaunchAsync returns.
        _ = Task.Run(() => Redirect(redirect, response, cancellationToken), cancellationToken);
        return Result.Success();
    }

    private static async Task Redirect(
        Uri redirect,
        string query,
        CancellationToken cancellationToken
    )
    {
        using (var client = new TcpClient())
        {
            await client.ConnectAsync(redirect.Host, redirect.Port, cancellationToken);
            var request =
                $"GET {redirect.AbsolutePath}?{query} HTTP/1.1\r\nHost: {redirect.Host}:{redirect.Port}\r\nConnection: close\r\n\r\n";
            await using (var stream = client.GetStream())
            {
                await stream.WriteAsync(Encoding.ASCII.GetBytes(request), cancellationToken);
                await stream.FlushAsync(cancellationToken);
                var buffer = new byte[1024];
                while (await stream.ReadAsync(buffer, cancellationToken) > 0) { }
            }
        }
    }

    private static Dictionary<string, string> Parse(string query) =>
        query
            .TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(
                pair => Uri.UnescapeDataString(pair[0]),
                pair =>
                    pair.Length > 1
                        ? Uri.UnescapeDataString(pair[1].Replace('+', ' '))
                        : string.Empty,
                StringComparer.Ordinal
            );
}
