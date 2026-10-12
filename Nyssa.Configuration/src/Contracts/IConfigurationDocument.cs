using NyssaAI.Results;

namespace NyssaAI.Configuration.Contracts;

/// <summary>
/// IConfigurationDocument is the persisted entry set for one namespace. A modification runs under the
/// document's own exclusion, so a caller never reads, edits, and writes back as separate steps.
/// </summary>
internal interface IConfigurationDocument
{
    /// <summary>Reads every entry the document currently holds.</summary>
    /// <param name="cancellationToken">Token used to cancel the read.</param>
    ValueTask<Result<IReadOnlyDictionary<string, ConfigurationValue>>> ReadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies a change to the entry set and persists the result atomically. The document is not
    /// written when the change reports failure.
    /// </summary>
    /// <param name="change">The mutation to apply to the loaded entry set.</param>
    /// <param name="cancellationToken">Token used to cancel the modification.</param>
    ValueTask<Result> ModifyAsync(
        Func<IDictionary<string, ConfigurationValue>, ValueTask<Result>> change,
        CancellationToken cancellationToken = default);
}
