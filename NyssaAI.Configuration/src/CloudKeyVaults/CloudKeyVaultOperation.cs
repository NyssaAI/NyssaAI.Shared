using NyssaAI.Configuration.Contracts;

namespace NyssaAI.Configuration.CloudKeyVaults;

/// <summary>The frozen intent associated with one stable request identity. Contains sensitive data; never log it.</summary>
public sealed record CloudKeyVaultOperation
{
    /// <summary>The deduplication identity, unchanged across every attempt.</summary>
    public required Guid OperationId { get; init; }
    /// <summary>The requested mutation.</summary>
    public required CloudKeyVaultAction Action { get; init; }
    /// <summary>The logical configuration namespace.</summary>
    public required string Namespace { get; init; }
    /// <summary>The logical configuration key.</summary>
    public required string Key { get; init; }
    /// <summary>The exact value to write; null for deletion.</summary>
    public string? Value { get; init; }
    /// <summary>The existing target, when one is known. Deletion always supplies it.</summary>
    public ConfigurationPointer? Pointer { get; init; }
    /// <summary>Redacts payloads and target addresses.</summary>
    public override string ToString() => $"CloudKeyVaultOperation {{ OperationId = {OperationId}, Action = {Action} }}";
}
