using NyssaAI.Configuration.Contracts;

namespace NyssaAI.Configuration.Client;

/// <summary>
/// Options configuring out-of-band secret capture.
/// </summary>
public sealed class CaptureSecretOptions
{
    /// <summary>Prompt message or field label presented to the user.</summary>
    public string? PromptMessage { get; set; }

    /// <summary>Preferred storage provider (e.g. "aesLocal" or a loaded cloud vault).</summary>
    public string? PreferredProvider { get; set; }

    /// <summary>Allowed storage providers that the user can choose between.</summary>
    public IReadOnlyList<string>? AllowedProviders { get; set; }

    /// <summary>Whether to prompt the user twice to verify/confirm match against typos. Defaults to true.</summary>
    public bool Confirm { get; set; } = true;

    /// <summary>Capture mode: Auto, Cli, or Web. Defaults to Auto.</summary>
    public SecretCaptureMode Mode { get; set; } = SecretCaptureMode.Auto;

    /// <summary>Maximum wait duration for user input. Defaults to 2 minutes if omitted.</summary>
    public TimeSpan? Timeout { get; set; }

    /// <summary>Explicit secret input provider implementation (primarily used for testing or custom host integration).</summary>
    public ISecretInputProvider? InputProvider { get; set; }
}
