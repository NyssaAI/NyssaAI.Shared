using NyssaAI.Results;

namespace NyssaAI.Configuration.Recovery;

/// <summary>Protects caller-owned continuation state, scoped to an application and canonical configuration directory.</summary>
internal interface IRecovery
{
    Result<ConfigurationRecoveryHandle> Seal(ConfigurationRecoveryState state);
    Result<ConfigurationRecoveryState> Open(ConfigurationRecoveryHandle handle);
}
