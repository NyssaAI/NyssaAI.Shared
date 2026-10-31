namespace NyssaAI.Configuration;

/// <summary>
/// IConfigurationExtensions is the second audience's contract: the operations an application
/// runs at set-up time, when a browser is acceptable, rather than on every read. A class that
/// only reads and writes settings depends on <see cref="IConfiguration"/> and never sees these.
/// </summary>
/// <remarks>
/// All three open a browser through the interactive OAuth flow. None of them is ever reached from
/// <see cref="IConfiguration"/>: a read that needs sign-in throws, and the
/// application decides where and when a person can act on it.
/// </remarks>
public interface IConfigurationExtensions : IConfigurationOAuthWriter
{
}
