using Microsoft.Extensions.Logging;
using NyssaAI.Results;

using NyssaAI.Configuration.Contracts;

namespace NyssaAI.Configuration.Values;

/// <summary>The one implementation of <see cref="IValues"/>, backed by local JSON documents.</summary>
internal sealed class LocalValues : IValues
{
    /// <inheritdoc/>
    public IConfigurationDocumentFactory OpenDocuments(string configurationDirectory, TimeSpan? lockTimeout = null) =>
        new ConfigurationDocumentFactoryLocalFile(configurationDirectory, lockTimeout);

    /// <inheritdoc/>
    public Result<IConfigurationValues> OpenStore(
        IConfigurationDocumentFactory documents,
        IEnumerable<IConfigurationProvider> providers,
        ILogger? logger = null)
    {
        var store = ConfigurationStore.Create(documents, providers, logger);
        return store.IsFailure
            ? Result<IConfigurationValues>.Failure(store.Errors)
            : Result<IConfigurationValues>.Success(store.Value);
    }
}
