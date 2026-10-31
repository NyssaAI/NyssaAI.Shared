namespace NyssaAI.Configuration.Recovery;

/// <summary>Whether the intended namespace document was published.</summary>
public enum ConfigurationPublicationState
{
    /// <summary>Publication has not begun.</summary>
    NotAttempted,
    /// <summary>The document was published.</summary>
    Published,
    /// <summary>The document could not be published.</summary>
    Failed,
    /// <summary>Publication has begun but its result is uncertain.</summary>
    Unknown,
}
