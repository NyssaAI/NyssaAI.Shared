namespace NyssaAI.Configuration.CloudKeyVaults;

/// <summary>Provider-owned evidence frozen before the first mutation and protected inside the recovery handle.</summary>
public sealed record CloudKeyVaultPreparation
{
    /// <summary>The actual backend identity, equal to ICloudKeyVaultRecovery.StoreIdentity.</summary>
    public required string StoreIdentity { get; init; }
    /// <summary>Opaque serialized identity and concurrency evidence. Must not contain reusable authentication credentials.</summary>
    public required string Context { get; init; }
    /// <summary>Optional non-secret target description suitable for diagnostic receipts.</summary>
    public string? TargetReference { get; init; }
    /// <summary>Redacts the opaque provider context.</summary>
    public override string ToString() => "CloudKeyVaultPreparation { protected }";
}
