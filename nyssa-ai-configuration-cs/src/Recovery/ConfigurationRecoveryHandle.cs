namespace NyssaAI.Configuration.Recovery;

/// <summary>An opaque, authenticated recovery token. Persist only through explicit export.</summary>
/// <remarks>Import does not authenticate the token. RecoverAsync authenticates it against the application's key and configuration root before contacting providers.</remarks>
public sealed record ConfigurationRecoveryHandle
{
    private readonly string _token;
    private ConfigurationRecoveryHandle(string token) => _token = token;
    /// <summary>Imports a previously exported token for validation during recovery.</summary>
    public static ConfigurationRecoveryHandle Import(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        return new ConfigurationRecoveryHandle(token);
    }
    /// <summary>Exports the protected token for caller-owned persistence. Treat it as sensitive and never log it.</summary>
    public string Export() => _token;
    /// <summary>Returns a redacted description, never the token.</summary>
    public override string ToString() => "ConfigurationRecoveryHandle { protected }";
}
