using Microsoft.Extensions.Logging;
using NyssaAI.Results;

using NyssaAI.Configuration.Contracts;

namespace NyssaAI.Configuration.Values;

/// <summary>
/// IValues is the Values feature's covering contract: how a sibling feature or the client area
/// obtains the two things this feature owns — the documents entries persist in, and the store that
/// resolves an entry through the provider it names.
/// </summary>
/// <remarks>
/// <para>
/// This is not <see cref="IConfigurationValues"/>, and the difference is the audience.
/// <see cref="IConfigurationValues"/> is a public port in <c>Contracts/</c>: an outside implementer
/// replaces the entry store with it wholesale, and <c>ConfigurationRoot.Compose</c> accepts one.
/// This contract is internal, and is how the rest of <em>this</em> assembly reaches the feature's own
/// implementation without naming <see cref="ConfigurationStore"/> or
/// <see cref="ConfigurationDocumentFactoryLocalFile"/>.
/// </para>
/// <para>
/// Both members open something, because opening is the whole of what this feature offers a sibling:
/// once a store exists the caller holds <see cref="IConfigurationValues"/> and never comes back
/// here.
/// </para>
/// </remarks>
internal interface IValues
{
    /// <summary>Opens the local document set holding one configuration directory's namespaces.</summary>
    /// <param name="configurationDirectory">The directory the namespace documents live in.</param>
    /// <param name="lockTimeout">The bounded wait for a document lock; the feature default when null.</param>
    IConfigurationDocumentFactory OpenDocuments(string configurationDirectory, TimeSpan? lockTimeout = null);

    /// <summary>Opens the entry store over one document set and the providers it may resolve through.</summary>
    /// <param name="documents">The documents entries are persisted in.</param>
    /// <param name="providers">The providers available to resolve non-literal entries.</param>
    /// <param name="logger">The logger provider failures are reported to, if any.</param>
    /// <returns>A failure when a provider name is blank or duplicated.</returns>
    Result<IConfigurationValues> OpenStore(
        IConfigurationDocumentFactory documents,
        IEnumerable<IConfigurationProvider> providers,
        ILogger? logger = null);
}
