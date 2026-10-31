using NyssaAI.Configuration.Values;

namespace NyssaAI.Configuration.CloudKeyVaults;

/// <summary>Holds the store once it exists, for token sources created before it did.</summary>
internal sealed class StoreReference
{
    public ConfigurationStore? Current { get; set; }
}
