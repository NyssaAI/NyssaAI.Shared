using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using NyssaAI.Configuration.Contracts;
using NyssaAI.Results;

namespace NyssaAI.Configuration.SecretInput;

/// <summary>
/// Captures secrets via an ephemeral, single-use HTTP server listening on 127.0.0.1 with an unguessable session token.
/// </summary>
internal sealed class LoopbackSecretInputProvider : ISecretInputProvider, IDisposable
{
    private readonly IBrowserLauncher? _browserLauncher;
    private readonly TextWriter? _writer;
    private readonly TaskCompletionSource<Uri> _urlTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _cts = new();
    private TcpListener? _listener;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="LoopbackSecretInputProvider"/> class.
    /// </summary>
    /// <param name="browserLauncher">Optional browser launcher for opening the loopback form.</param>
    /// <param name="writer">Optional text writer for status output; defaults to Console.Out.</param>
    public LoopbackSecretInputProvider(IBrowserLauncher? browserLauncher = null, TextWriter? writer = null)
    {
        _browserLauncher = browserLauncher;
        _writer = writer ?? Console.Out;
        SessionToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
    }

    /// <summary>Cryptographically random 32-byte hex token safeguarding the session.</summary>
    public string SessionToken { get; }

    /// <summary>Wait for the listener to bind and return the authorization URL.</summary>
    public Task<Uri> WaitForUrlAsync(CancellationToken cancellationToken = default)
    {
        using (cancellationToken.Register(() => _urlTcs.TrySetCanceled(cancellationToken)))
        {
            return _urlTcs.Task;
        }
    }

    /// <summary>Manually cancel the pending loopback capture.</summary>
    public void Cancel() => _cts.Cancel();

    /// <inheritdoc/>
    public async ValueTask<Result<CapturedSecret>> CaptureAsync(
        SecretPromptRequest request,
        CancellationToken cancellationToken = default)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cts.Token);
        linkedCts.CancelAfter(request.Timeout);

        try
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();

            var port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            var localUri = new Uri($"http://127.0.0.1:{port}/?token={SessionToken}");
            _urlTcs.TrySetResult(localUri);

            _writer?.WriteLine();
            _writer?.WriteLine($"Secret input required for {request.Namespace}:{request.Key}.");
            _writer?.WriteLine($"Please navigate to the following URL to enter the secret:");
            _writer?.WriteLine(localUri.AbsoluteUri);
            _writer?.WriteLine("Waiting for secret submission in browser...");

            // Try opening default browser
            if (_browserLauncher is not null)
            {
                try
                {
                    await _browserLauncher.LaunchAsync(localUri, linkedCts.Token).ConfigureAwait(false);
                }
                catch
                {
                    // Fall back to terminal URL display without aborting
                }
            }

            while (!linkedCts.Token.IsCancellationRequested)
            {
                using var client = await _listener.AcceptTcpClientAsync(linkedCts.Token).ConfigureAwait(false);
                using var stream = client.GetStream();

                var buffer = new byte[16384];
                var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), linkedCts.Token).ConfigureAwait(false);
                if (bytesRead == 0) continue;

                var requestText = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                var firstLine = requestText.Split("\r\n", 2)[0];
                var parts = firstLine.Split(' ');
                if (parts.Length < 2) continue;

                var method = parts[0];
                var rawPath = parts[1];

                if (string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase))
                {
                    var tokenMatch = rawPath.Contains($"token={SessionToken}", StringComparison.Ordinal);
                    if (!tokenMatch)
                    {
                        await WriteResponseAsync(stream, 403, "Forbidden", "<html><body><h3>403 Forbidden: Invalid or missing session token.</h3></body></html>", linkedCts.Token).ConfigureAwait(false);
                        continue;
                    }

                    var html = RenderHtmlForm(request);
                    await WriteResponseAsync(stream, 200, "OK", html, linkedCts.Token).ConfigureAwait(false);
                }
                else if (string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase))
                {
                    // Parse form body
                    var headerBodySplit = requestText.Split("\r\n\r\n", 2);
                    var body = headerBodySplit.Length > 1 ? headerBodySplit[1] : string.Empty;

                    // If full body was not yet received, read remainder
                    var contentLengthHeader = headerBodySplit[0].Split("\r\n")
                        .FirstOrDefault(h => h.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase));
                    if (contentLengthHeader is not null && int.TryParse(contentLengthHeader.Split(':')[1].Trim(), out var expectedLength))
                    {
                        var bodyBytesCurrent = Encoding.UTF8.GetByteCount(body);
                        while (bodyBytesCurrent < expectedLength)
                        {
                            var more = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), linkedCts.Token).ConfigureAwait(false);
                            if (more == 0) break;
                            body += Encoding.UTF8.GetString(buffer, 0, more);
                            bodyBytesCurrent += more;
                        }
                    }

                    var formData = ParseFormEncoded(body);
                    formData.TryGetValue("session_token", out var submittedToken);
                    if (!string.Equals(submittedToken, SessionToken, StringComparison.Ordinal))
                    {
                        await WriteResponseAsync(stream, 403, "Forbidden", "<html><body><h3>403 Forbidden: Invalid session token.</h3></body></html>", linkedCts.Token).ConfigureAwait(false);
                        continue;
                    }

                    formData.TryGetValue("secret", out var secret);
                    formData.TryGetValue("confirm_secret", out var confirmSecret);
                    formData.TryGetValue("provider", out var chosenProvider);
                    if (string.IsNullOrWhiteSpace(chosenProvider))
                        chosenProvider = request.PreferredProvider;

                    if (request.Confirm && !string.Equals(secret, confirmSecret, StringComparison.Ordinal))
                    {
                        await WriteResponseAsync(stream, 400, "Bad Request", "<html><body style='font-family:sans-serif;margin:40px'><h3>Error: Secrets did not match. Please go back and retry.</h3></body></html>", linkedCts.Token).ConfigureAwait(false);
                        continue;
                    }

                    await WriteResponseAsync(stream, 200, "OK",
                        "<html><body style='font-family:sans-serif;margin:40px;background:#1e1e1e;color:#fff'><h2>Secret Saved Successfully</h2><p>You can close this tab now.</p></body></html>",
                        linkedCts.Token).ConfigureAwait(false);

                    _writer?.WriteLine("Secret received and captured successfully.");
                    return Result<CapturedSecret>.Success(new CapturedSecret((secret ?? string.Empty).ToCharArray(), chosenProvider));
                }
            }

            return Result<CapturedSecret>.Failure(Error.FromMessages("Secret capture timed out or was cancelled.", "Timeout waiting for user submission.", ErrorCategory.Unavailable));
        }
        catch (OperationCanceledException)
        {
            return Result<CapturedSecret>.Failure(Error.FromMessages("Secret capture cancelled.", "User cancelled operation.", ErrorCategory.Canceled));
        }
        catch (Exception ex)
        {
            return Result<CapturedSecret>.Failure(Error.FromMessages("Failed to capture secret via loopback listener.", ex.Message, ErrorCategory.Unexpected));
        }
        finally
        {
            Dispose();
        }
    }

    private static async Task WriteResponseAsync(NetworkStream stream, int statusCode, string statusReason, string body, CancellationToken token)
    {
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var header = $"HTTP/1.1 {statusCode} {statusReason}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {bodyBytes.Length}\r\nConnection: close\r\n\r\n";
        var headerBytes = Encoding.ASCII.GetBytes(header);

        await stream.WriteAsync(headerBytes.AsMemory(0, headerBytes.Length), token).ConfigureAwait(false);
        await stream.WriteAsync(bodyBytes.AsMemory(0, bodyBytes.Length), token).ConfigureAwait(false);
        await stream.FlushAsync(token).ConfigureAwait(false);
    }

    private string RenderHtmlForm(SecretPromptRequest request)
    {
        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html><html><head><meta charset='utf-8'><title>NyssaAI Secret Capture</title>");
        sb.Append("<style>body{font-family:-apple-system,BlinkMacSystemFont,Segoe UI,Roboto,sans-serif;margin:40px auto;max-width:540px;background:#18181b;color:#f4f4f5;padding:24px}input,select,button{width:100%;padding:10px;margin:8px 0 16px;border-radius:6px;border:1px solid #3f3f46;background:#27272a;color:#fff;box-sizing:border-box;font-size:14px}button{background:#2563eb;color:#fff;font-weight:600;border:none;cursor:pointer}button:hover{background:#1d4ed8}.badge{background:#3f3f46;padding:2px 8px;border-radius:4px;font-family:monospace;font-size:13px}</style></head><body>");
        sb.Append("<h2>NyssaAI Configuration</h2>");
        sb.Append($"<p>Target: <span class='badge'>{request.Namespace}:{request.Key}</span></p>");
        if (!string.IsNullOrWhiteSpace(request.PromptMessage))
            sb.Append($"<p style='color:#a1a1aa'>{WebUtility.HtmlEncode(request.PromptMessage)}</p>");

        sb.Append("<form method='POST' onsubmit=\"var s=document.querySelector('input[name=secret]').value; var c=document.querySelector('input[name=confirm_secret]'); if(c &amp;&amp; s!==c.value){ alert('Entered secrets do not match. Please verify.'); return false; }\">");
        sb.Append($"<input type='hidden' name='session_token' value='{SessionToken}' />");

        if (request.AvailableProviders.Count == 1)
        {
            var fixedProv = request.AvailableProviders[0];
            var label = FormatProviderLabel(fixedProv);
            sb.Append("<label>Storage Destination (Fixed by Application):</label>");
            sb.Append($"<input type='text' value='{WebUtility.HtmlEncode(label)}' disabled style='background:#3f3f46;color:#a1a1aa' />");
            sb.Append($"<input type='hidden' name='provider' value='{WebUtility.HtmlEncode(fixedProv)}' />");
        }
        else
        {
            sb.Append("<label>Storage Destination:</label><select name='provider'>");
            foreach (var prov in request.AvailableProviders)
            {
                var isSelected = string.Equals(prov, request.PreferredProvider, StringComparison.OrdinalIgnoreCase) ? " selected" : "";
                var label = FormatProviderLabel(prov);
                sb.Append($"<option value='{WebUtility.HtmlEncode(prov)}'{isSelected}>{WebUtility.HtmlEncode(label)}</option>");
            }
            sb.Append("</select>");
        }

        sb.Append("<label>Secret Value:</label><input type='password' name='secret' required autofocus />");
        if (request.Confirm)
            sb.Append("<label>Confirm Secret:</label><input type='password' name='confirm_secret' required />");

        sb.Append("<button type='submit'>Save Secret</button>");
        sb.Append("</form></body></html>");
        return sb.ToString();
    }

    private static string FormatProviderLabel(string name)
    {
        var lower = name.ToLowerInvariant();
        if (lower == "aeslocal") return "Encrypted Locally (AES-256-GCM)";
        if (lower is "literal" or "default" or "plaintext" or "") return "Free Text (Unencrypted Plaintext)";
        if (lower is "bws" or "bitwarden" or "bitwarden-secrets") return "Key Vault (Bitwarden Secrets Manager)";
        if (lower.StartsWith("azure", StringComparison.Ordinal)) return "Key Vault (Azure Key Vault)";
        if (lower.StartsWith("hashi", StringComparison.Ordinal) || lower == "vault") return "Key Vault (HashiCorp Vault)";
        return $"Key Vault ({name})";
    }

    private static Dictionary<string, string> ParseFormEncoded(string body)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var pairs = body.Split('&', StringSplitOptions.RemoveEmptyEntries);
        foreach (var pair in pairs)
        {
            var kv = pair.Split('=', 2);
            var key = Uri.UnescapeDataString(kv[0].Replace('+', ' '));
            var val = kv.Length > 1 ? Uri.UnescapeDataString(kv[1].Replace('+', ' ')) : string.Empty;
            dict[key] = val;
        }
        return dict;
    }

    /// <summary>
    /// Disposes the loopback listener and releases its socket resources.
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            _cts.Cancel();
            try { _listener?.Stop(); } catch { }
            _disposed = true;
        }
    }
}
