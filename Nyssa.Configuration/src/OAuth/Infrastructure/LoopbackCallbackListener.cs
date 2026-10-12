using System.Net;
using System.Net.Sockets;
using System.Text;
using NyssaAI.Results;

namespace NyssaAI.Configuration.OAuth.Infrastructure;

/// <summary>
/// LoopbackCallbackListener accepts exactly one authorization callback on a loopback port, and
/// nothing else. It binds <see cref="IPAddress.Loopback"/> and <see cref="IPAddress.IPv6Loopback"/>
/// on one port -- never a wildcard address, which would expose the callback to the network --
/// answers any request that is not the callback and keeps waiting, and releases the port on every
/// exit path.
/// </summary>
/// <remarks>
/// A raw <see cref="TcpListener"/> rather than <c>HttpListener</c>: the callback is one GET
/// request line and a fixed response, and <c>HttpListener</c> is documented as not recommended
/// for new development and carries no trimming or AOT contract. On Windows the socket is opened
/// with <c>SO_EXCLUSIVEADDRUSE</c> so no other local process can claim the port; on Linux the
/// kernel already refuses a second listener and <c>SO_REUSEADDR</c> is left unset.
/// </remarks>
internal sealed class LoopbackCallbackListener : IDisposable
{
    private const int MaxRequestBytes = 16 * 1024;
    private static readonly TimeSpan RequestReadTimeout = TimeSpan.FromSeconds(5);

    private readonly List<TcpListener> _listeners;
    private readonly string _callbackPath;
    private bool _closed;

    private LoopbackCallbackListener(List<TcpListener> listeners, int port, string callbackPath, bool ipv4Bound)
    {
        _listeners = listeners;
        _callbackPath = callbackPath;
        Port = port;
        RedirectUri = new Uri(ipv4Bound
            ? $"http://127.0.0.1:{port}{callbackPath}"
            : $"http://[::1]:{port}{callbackPath}");
    }

    /// <summary>Gets the port the listener is bound to.</summary>
    public int Port { get; }

    /// <summary>Gets the redirect URI to send to the authorization server.</summary>
    public Uri RedirectUri { get; }

    /// <summary>
    /// Binds the listener. Both loopback families are attempted on one port; the listener is
    /// usable when at least one binds. An ephemeral port is chosen when <paramref name="port"/>
    /// is null.
    /// </summary>
    /// <param name="port">The port to bind, or null for an ephemeral one.</param>
    /// <param name="callbackPath">The request path that counts as the callback.</param>
    public static Result<LoopbackCallbackListener> Create(int? port = null, string callbackPath = "/callback")
    {
        if (string.IsNullOrEmpty(callbackPath) || callbackPath[0] != '/')
            return Result<LoopbackCallbackListener>.Failure(CallbackPathInvalid(callbackPath));

        var listeners = new List<TcpListener>(2);
        var bound = 0;
        var ipv4Bound = false;
        Exception? lastFailure = null;

        // This is the adapter boundary: Bind and Start throw when a port is taken or a family is
        // absent, and a single family binding is enough.
        foreach (var address in new[] { IPAddress.Loopback, IPAddress.IPv6Loopback })
        {
            try
            {
                var listener = new TcpListener(address, bound != 0 ? bound : port ?? 0);
                if (OperatingSystem.IsWindows())
                    listener.ExclusiveAddressUse = true;

                listener.Start();
                bound = ((IPEndPoint)listener.LocalEndpoint).Port;
                ipv4Bound |= address.AddressFamily == AddressFamily.InterNetwork;
                listeners.Add(listener);
            }
            catch (SocketException exception)
            {
                lastFailure = exception;
            }
        }

        if (listeners.Count == 0)
            return Result<LoopbackCallbackListener>.Failure(BindFailed(port, lastFailure));

        return Result<LoopbackCallbackListener>.Success(new LoopbackCallbackListener(listeners, bound, callbackPath, ipv4Bound));
    }

    /// <summary>
    /// Waits for the callback and returns its query values. Any other request is answered with
    /// <c>404</c> and discarded. The port is released before this returns, whatever the outcome.
    /// </summary>
    /// <param name="cancellationToken">Token used to stop waiting.</param>
    public async ValueTask<Result<IReadOnlyDictionary<string, string>>> WaitForCallbackAsync(CancellationToken cancellationToken = default)
    {
        if (_closed)
            return Result<IReadOnlyDictionary<string, string>>.Failure(Closed);

        // This is the adapter boundary: the sockets throw on cancellation and on I/O faults, and
        // the listener is closed in the finally so every exit path releases the port.
        try
        {
            var pending = _listeners.ToDictionary(listener => listener, listener => listener.AcceptTcpClientAsync(cancellationToken).AsTask());

            while (true)
            {
                var completed = await Task.WhenAny(pending.Values).ConfigureAwait(false);
                var listener = pending.First(pair => pair.Value == completed).Key;
                using var client = await completed.ConfigureAwait(false);
                pending[listener] = listener.AcceptTcpClientAsync(cancellationToken).AsTask();

                var query = await HandleAsync(client, cancellationToken).ConfigureAwait(false);
                if (query is not null)
                    return Result<IReadOnlyDictionary<string, string>>.Success(query);
            }
        }
        catch (OperationCanceledException canceled)
        {
            return Result<IReadOnlyDictionary<string, string>>.Failure(Error.Canceled(canceled));
        }
        catch (Exception exception) when (exception is SocketException or IOException or ObjectDisposedException)
        {
            return Result<IReadOnlyDictionary<string, string>>.Failure(ListenFailed(exception));
        }
        finally
        {
            Dispose();
        }
    }

    /// <summary>Stops listening and releases the port. Idempotent.</summary>
    public void Dispose()
    {
        if (_closed)
            return;

        _closed = true;
        foreach (var listener in _listeners)
        {
            // This is the adapter boundary: a listener that never started throws on Stop.
            try
            {
                listener.Stop();
            }
            catch (SocketException)
            {
                // Releasing the port is best effort once the flow is over.
            }
        }
    }

    /// <summary>The failure returned when neither loopback family could be bound.</summary>
    /// <param name="port">The port that was requested, or null for ephemeral.</param>
    /// <param name="exception">The last bind failure.</param>
    public static Error BindFailed(int? port, Exception? exception) => Error.FromMessages(
        "The sign-in listener could not start.",
        $"oauth: could not bind a loopback listener on port {(port is null ? "(ephemeral)" : port.ToString())}: {exception?.Message}",
        ErrorCategory.Unexpected);

    /// <summary>The failure returned when the listener faulted while waiting.</summary>
    /// <param name="exception">The underlying socket failure.</param>
    public static Error ListenFailed(Exception exception) => Error.FromMessages(
        "The sign-in listener failed.",
        $"oauth: the loopback listener failed: {exception.GetType().Name}: {exception.Message}",
        ErrorCategory.Unexpected);

    /// <summary>The failure returned when the listener is used after it was closed.</summary>
    public static Error Closed { get; } = Error.FromMessages(
        "The sign-in listener is closed.",
        "oauth: the loopback listener has already been closed",
        ErrorCategory.Conflict);

    /// <summary>The failure returned when the callback path does not start with a slash.</summary>
    /// <param name="callbackPath">The path that was supplied.</param>
    public static Error CallbackPathInvalid(string callbackPath) => Error.FromMessages(
        "The sign-in listener could not start.",
        $"oauth: callback path '{callbackPath}' must start with '/'",
        ErrorCategory.Validation);

    // Reads one request, answers it, and returns the query when it was the callback.
    private async Task<Dictionary<string, string>?> HandleAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using var stream = client.GetStream();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestReadTimeout);

        string? requestLine;
        try
        {
            requestLine = await ReadRequestLineAsync(stream, timeout.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or SocketException
            || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return null;
        }

        if (requestLine is null)
            return null;

        var parts = requestLine.Split(' ');
        if (parts.Length < 2 || !string.Equals(parts[0], "GET", StringComparison.Ordinal))
        {
            await RespondAsync(stream, "405 Method Not Allowed", "Not the sign-in callback.", cancellationToken).ConfigureAwait(false);
            return null;
        }

        var target = parts[1];
        var separator = target.IndexOf('?', StringComparison.Ordinal);
        var path = separator < 0 ? target : target[..separator];
        if (!string.Equals(path, _callbackPath, StringComparison.Ordinal))
        {
            await RespondAsync(stream, "404 Not Found", "Not the sign-in callback.", cancellationToken).ConfigureAwait(false);
            return null;
        }

        var query = ParseQuery(separator < 0 ? string.Empty : target[(separator + 1)..]);
        await RespondAsync(stream, "200 OK", "Sign-in complete. You can close this window.", cancellationToken).ConfigureAwait(false);
        return query;
    }

    // Reads up to the end of the headers, bounded, and returns the first line. Reading the whole
    // request before answering keeps a browser from seeing a reset on a half-read request.
    private static async Task<string?> ReadRequestLineAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[MaxRequestBytes];
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total), cancellationToken).ConfigureAwait(false);
            if (read == 0)
                break;

            total += read;
            var text = Encoding.ASCII.GetString(buffer, 0, total);
            if (text.Contains("\r\n\r\n", StringComparison.Ordinal) || text.Contains("\n\n", StringComparison.Ordinal))
                break;
        }

        if (total == 0)
            return null;

        var request = Encoding.ASCII.GetString(buffer, 0, total);
        var lineEnd = request.IndexOf('\n', StringComparison.Ordinal);
        return (lineEnd < 0 ? request : request[..lineEnd]).TrimEnd('\r');
    }

    private static async Task RespondAsync(NetworkStream stream, string status, string message, CancellationToken cancellationToken)
    {
        var body = Encoding.UTF8.GetBytes($"<!doctype html><html><body><p>{message}</p></body></html>");
        var head = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {status}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
        try
        {
            await stream.WriteAsync(head, cancellationToken).ConfigureAwait(false);
            await stream.WriteAsync(body, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or SocketException)
        {
            // The peer went away; the answer was a courtesy.
        }
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = pair.IndexOf('=', StringComparison.Ordinal);
            var name = Uri.UnescapeDataString((equals < 0 ? pair : pair[..equals]).Replace('+', ' '));
            var value = equals < 0 ? string.Empty : Uri.UnescapeDataString(pair[(equals + 1)..].Replace('+', ' '));
            values[name] = value;
        }

        return values;
    }
}
