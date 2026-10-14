using NyssaAI.Configuration.Contracts;
using NyssaAI.Results;

namespace NyssaAI.Configuration.Tests.Support;

internal sealed class FailingPublicationFactory : IConfigurationDocumentFactory
{
    private readonly IConfigurationDocumentFactory _inner;
    public int FailuresRemaining { get; set; }
    public bool FailNext { get => FailuresRemaining > 0; set => FailuresRemaining = value ? 1 : 0; }
    public IReadOnlyDictionary<string, ConfigurationValue>? FailedEntries { get; private set; }
    public FailingPublicationFactory(IConfigurationDocumentFactory inner) => _inner = inner;
    public Result<IConfigurationDocument> Create(string @namespace)
    {
        var opened = _inner.Create(@namespace);
        return opened.IsFailure ? Result<IConfigurationDocument>.Failure(opened.Errors)
            : Result<IConfigurationDocument>.Success(new FailingPublication(this, opened.Value));
    }
    private sealed class FailingPublication : IConfigurationDocument
    {
        private readonly FailingPublicationFactory _owner;
        private readonly IConfigurationDocument _inner;
        public FailingPublication(FailingPublicationFactory owner, IConfigurationDocument inner) { _owner = owner; _inner = inner; }
        public ValueTask<Result<IReadOnlyDictionary<string, ConfigurationValue>>> ReadAsync(CancellationToken cancellationToken = default) =>
            _inner.ReadAsync(cancellationToken);
        public ValueTask<Result> ModifyAsync(Func<IDictionary<string, ConfigurationValue>, ValueTask<Result>> change, CancellationToken cancellationToken = default) =>
            _inner.ModifyAsync(async entries =>
            {
                var changed = await change(entries);
                if (changed.IsSuccess && _owner.FailuresRemaining > 0)
                {
                    _owner.FailuresRemaining--;
                    _owner.FailedEntries = new Dictionary<string, ConfigurationValue>(entries);
                    return Result.Failure(Error.FromMessage("Publication failed", ErrorCategory.Unavailable));
                }
                return changed;
            }, cancellationToken);
    }
}
