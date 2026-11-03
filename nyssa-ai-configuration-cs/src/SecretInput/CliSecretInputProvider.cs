using System.Text;
using NyssaAI.Configuration.Contracts;
using NyssaAI.Results;

namespace NyssaAI.Configuration.SecretInput;

/// <summary>
/// Captures secrets directly from the terminal console using masked characters.
/// </summary>
internal sealed class CliSecretInputProvider : ISecretInputProvider
{
    private readonly TextReader _reader;
    private readonly TextWriter _writer;

    /// <summary>
    /// Initializes a new instance of the <see cref="CliSecretInputProvider"/> class.
    /// </summary>
    /// <param name="reader">Optional text reader for terminal input; defaults to Console.In.</param>
    /// <param name="writer">Optional text writer for terminal output; defaults to Console.Out.</param>
    public CliSecretInputProvider(TextReader? reader = null, TextWriter? writer = null)
    {
        _reader = reader ?? Console.In;
        _writer = writer ?? Console.Out;
    }

    /// <inheritdoc/>
    public ValueTask<Result<CapturedSecret>> CaptureAsync(
        SecretPromptRequest request,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
            return ValueTask.FromCanceled<Result<CapturedSecret>>(cancellationToken);

        try
        {
            _writer.WriteLine();
            var title = string.IsNullOrWhiteSpace(request.PromptMessage)
                ? $"Enter secret for {request.Namespace}:{request.Key}"
                : $"{request.PromptMessage} ({request.Namespace}:{request.Key})";
            _writer.WriteLine(title);

            // Provider selection
            var selectedProvider = request.PreferredProvider;
            if (request.AvailableProviders.Count == 1)
            {
                var fixedProv = request.AvailableProviders[0];
                var label = FormatProviderLabel(fixedProv);
                _writer.WriteLine($"Storage destination: {label} [{fixedProv}] (Fixed by application)");
            }
            else if (request.AvailableProviders.Count > 1)
            {
                _writer.WriteLine("Select storage destination:");
                var defaultIdx = 1;
                for (var i = 0; i < request.AvailableProviders.Count; i++)
                {
                    var prov = request.AvailableProviders[i];
                    var isDef = string.Equals(prov, request.PreferredProvider, StringComparison.OrdinalIgnoreCase);
                    if (isDef) defaultIdx = i + 1;
                    var label = FormatProviderLabel(prov);
                    _writer.WriteLine($"  [{i + 1}] {label} [{prov}]{(isDef ? " (Default)" : "")}");
                }

                _writer.Write($"Destination [{defaultIdx}]: ");
                var choiceLine = _reader.ReadLine();
                if (!string.IsNullOrWhiteSpace(choiceLine) && int.TryParse(choiceLine.Trim(), out var choiceNum)
                    && choiceNum >= 1 && choiceNum <= request.AvailableProviders.Count)
                {
                    selectedProvider = request.AvailableProviders[choiceNum - 1];
                }
            }

            // Read secret
            _writer.Write("Enter secret: ");
            var firstEntry = ReadMaskedLine();

            if (request.Confirm)
            {
                _writer.Write("Confirm secret: ");
                var secondEntry = ReadMaskedLine();

                if (!string.Equals(firstEntry, secondEntry, StringComparison.Ordinal))
                {
                    return ValueTask.FromResult(Result<CapturedSecret>.Failure(
                        Error.FromMessages(
                            "The entered secrets do not match.",
                            "Secret confirmation mismatch in CLI prompt.",
                            ErrorCategory.Validation)));
                }
            }

            var secretChars = firstEntry.ToCharArray();
            return ValueTask.FromResult(Result<CapturedSecret>.Success(new CapturedSecret(secretChars, selectedProvider)));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ValueTask.FromResult(Result<CapturedSecret>.Failure(
                Error.FromMessages(
                    "Failed to read secret from terminal input.",
                    ex.Message,
                    ErrorCategory.Unexpected)));
        }
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

    private string ReadMaskedLine()
    {
        // When running in an interactive Windows/POSIX console attached to standard in
        if (ReferenceEquals(_reader, Console.In) && !Console.IsInputRedirected)
        {
            var sb = new StringBuilder();
            while (true)
            {
                var key = Console.ReadKey(intercept: true);
                if (key.Key == ConsoleKey.Enter)
                {
                    _writer.WriteLine();
                    break;
                }

                if (key.Key == ConsoleKey.Backspace)
                {
                    if (sb.Length > 0)
                    {
                        sb.Length--;
                        _writer.Write("\b \b");
                    }
                }
                else if (!char.IsControl(key.KeyChar))
                {
                    sb.Append(key.KeyChar);
                    _writer.Write('*');
                }
            }
            return sb.ToString();
        }

        // Piped/StringReader fallback for testing or non-console TextReaders
        var line = _reader.ReadLine() ?? string.Empty;
        return line.TrimEnd('\r', '\n');
    }
}
