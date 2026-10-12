using NyssaAI.Results;

namespace NyssaAI.Configuration.Contracts;

/// <summary>
/// IConfigurationDocumentFactory opens the <see cref="IConfigurationDocument"/> that holds one namespace,
/// failing when the namespace is not a name the backing store can address.
/// </summary>
internal interface IConfigurationDocumentFactory
{
    /// <summary>Opens the document for one namespace.</summary>
    /// <param name="namespace">The namespace whose document to open.</param>
    Result<IConfigurationDocument> Create(string @namespace);
}
