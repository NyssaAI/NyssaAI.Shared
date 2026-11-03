using NyssaAI.Configuration.Client;
using NyssaAI.Configuration.Contracts;
using NyssaAI.Results;

namespace NyssaAI.Configuration.SecretInput;

/// <summary>
/// Selects between CLI masked console input and ephemeral Web loopback based on terminal interactivity.
/// </summary>
internal sealed class AdaptiveSecretInputProvider : ISecretInputProvider
{
    private readonly SecretCaptureMode _mode;
    private readonly IBrowserLauncher? _browserLauncher;
    private readonly TextReader? _reader;
    private readonly TextWriter? _writer;

    /// <summary>
    /// Initializes a new instance of the <see cref="AdaptiveSecretInputProvider"/> class.
    /// </summary>
    /// <param name="mode">Capture mode override.</param>
    /// <param name="browserLauncher">Optional browser launcher for web loopback.</param>
    /// <param name="reader">Optional reader for console input.</param>
    /// <param name="writer">Optional writer for console output.</param>
    public AdaptiveSecretInputProvider(
        SecretCaptureMode mode = SecretCaptureMode.Auto,
        IBrowserLauncher? browserLauncher = null,
        TextReader? reader = null,
        TextWriter? writer = null)
    {
        _mode = mode;
        _browserLauncher = browserLauncher;
        _reader = reader;
        _writer = writer;
    }

    /// <inheritdoc/>
    public ValueTask<Result<CapturedSecret>> CaptureAsync(
        SecretPromptRequest request,
        CancellationToken cancellationToken = default)
    {
        var resolvedMode = _mode;
        if (resolvedMode == SecretCaptureMode.Auto)
        {
            var isPipedOrRedirected = Console.IsInputRedirected || !Environment.UserInteractive;
            resolvedMode = isPipedOrRedirected ? SecretCaptureMode.Web : SecretCaptureMode.Cli;
        }

        if (resolvedMode == SecretCaptureMode.Cli)
        {
            var cli = new CliSecretInputProvider(_reader, _writer);
            return cli.CaptureAsync(request, cancellationToken);
        }
        else
        {
            var loopback = new LoopbackSecretInputProvider(_browserLauncher, _writer);
            return loopback.CaptureAsync(request, cancellationToken);
        }
    }
}
