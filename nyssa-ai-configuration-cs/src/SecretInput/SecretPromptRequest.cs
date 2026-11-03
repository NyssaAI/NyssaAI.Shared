using System.Diagnostics.CodeAnalysis;

namespace NyssaAI.Configuration.Contracts;

/// <summary>
/// Data contract passed to an <see cref="ISecretInputProvider"/> to describe the secret capture request.
/// </summary>
public sealed record SecretPromptRequest
{
    /// <summary>Target namespace.</summary>
    public required string Namespace { get; init; }

    /// <summary>Target key.</summary>
    public required string Key { get; init; }

    /// <summary>Prompt message or field title.</summary>
    public required string PromptMessage { get; init; }

    /// <summary>List of storage providers available for destination selection.</summary>
    public required IReadOnlyList<string> AvailableProviders { get; init; }

    /// <summary>Default or preferred destination provider.</summary>
    public required string PreferredProvider { get; init; }

    /// <summary>Whether to prompt twice for verification.</summary>
    public required bool Confirm { get; init; }

    /// <summary>Timeout duration for capturing the secret.</summary>
    public required TimeSpan Timeout { get; init; }

    /// <summary>Initializes a new instance of <see cref="SecretPromptRequest"/>.</summary>
    [SetsRequiredMembers]
    public SecretPromptRequest(
        string @namespace,
        string key,
        string promptMessage,
        IReadOnlyList<string> availableProviders,
        string preferredProvider,
        bool confirm,
        TimeSpan timeout)
    {
        Namespace = @namespace;
        Key = key;
        PromptMessage = promptMessage;
        AvailableProviders = availableProviders;
        PreferredProvider = preferredProvider;
        Confirm = confirm;
        Timeout = timeout;
    }

    /// <summary>Initializes a new instance of <see cref="SecretPromptRequest"/>.</summary>
    public SecretPromptRequest()
    {
    }
}
