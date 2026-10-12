using System.Reflection;
using Xunit;

namespace NyssaAI.Results.Tests;

public sealed class PublicSurfaceTests
{
    [Theory]
    [InlineData(typeof(Result))]
    [InlineData(typeof(Result<int>))]
    public void Failure_OnEitherResultType_AcceptsOnlyMessageErrorOrErrorCollection(Type resultType)
    {
        var factories = resultType.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => method.Name == "Failure")
            .ToArray();

        Assert.Equal(3, factories.Length);
        var argumentTypes = factories.Select(method => Assert.Single(method.GetParameters()).ParameterType)
            .ToHashSet();
        Assert.True(argumentTypes.SetEquals([typeof(string), typeof(Error), typeof(IEnumerable<Error>)]));
    }

    [Theory]
    [InlineData(typeof(Result))]
    [InlineData(typeof(Result<int>))]
    public void PublicMembers_OnEitherResultType_ExcludeRemovedConveniences(Type resultType)
    {
        Assert.DoesNotContain(resultType.GetMethods(), method =>
            method.Name is "Canceled" or "TryGetErrors");
    }
}
