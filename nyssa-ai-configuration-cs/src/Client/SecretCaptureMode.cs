namespace NyssaAI.Configuration.Client;

/// <summary>
/// Operational mode for capturing secrets out-of-band.
/// </summary>
public enum SecretCaptureMode
{
    /// <summary>
    /// Uses CLI masked prompt when standard input is an interactive TTY; falls back to Web loopback if headless or piped.
    /// </summary>
    Auto,

    /// <summary>
    /// Strictly requires direct masked terminal input.
    /// </summary>
    Cli,

    /// <summary>
    /// Strictly uses an ephemeral local loopback HTTP server with a browser prompt.
    /// </summary>
    Web
}
