using NyssaAI.Configuration.Recovery;
using NyssaAI.Results;

namespace NyssaAI.Configuration.Tests.Recovery;

public sealed class ConfigurationOperationExceptionTests
{
    [Fact]
    public void Exceptions_PartialOperation_PreserveBaseCatchesAndCancellationToken()
    {
        var error = Error.FromMessage("Unavailable", ErrorCategory.Unavailable);
        var progress = new ConfigurationOperationProgress
        {
            OperationId = Guid.NewGuid(), Namespace = "mail", Kind = ConfigurationOperationKind.Move,
            Steps = [], Publication = ConfigurationPublicationState.Published,
        };
        var exception = new ConfigurationOperationException([error], progress, null);
        Assert.IsAssignableFrom<ResultException>(exception);
        Assert.Same(error, Assert.Single(exception.Errors));
        Assert.Same(progress, exception.Progress);

        using var cancellation = new CancellationTokenSource();
        var canceled = new ConfigurationOperationCanceledException(progress, null, cancellation.Token);
        Assert.IsAssignableFrom<OperationCanceledException>(canceled);
        Assert.Equal(cancellation.Token, canceled.CancellationToken);
    }
}
