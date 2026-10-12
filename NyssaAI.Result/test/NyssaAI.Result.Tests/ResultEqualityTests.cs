using Xunit;

namespace NyssaAI.Results.Tests;

// Both result types are records, so they carry an equality contract whether or not one was
// designed; these pin it to value semantics over the payload and the error sequence, so that
// Assert.Equal(expected, actual) and dictionary keying behave the same for successes and failures.
public sealed class ResultEqualityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Failure_ExposedErrors_CannotBeReplaced(bool generic)
    {
        var expected = Error.FromMessage("Original failure");
        var errors = generic ? Result<int>.Failure(expected).Errors : Result.Failure(expected).Errors;

        var collection = Assert.IsAssignableFrom<IList<Error>>(errors);
        Assert.Throws<NotSupportedException>(() => collection[0] = Error.FromMessage("Replacement"));
        Assert.Equal(expected, Assert.Single(errors));
    }

    [Fact]
    public void Try_AggregateFailure_CannotBeChangedIntoSuccessThroughItsErrors()
    {
        var result = Result.Try(() => throw new AggregateException(
            new InvalidOperationException("First failure"), new IOException("Second failure")));

        var collection = Assert.IsAssignableFrom<ICollection<Error>>(result.Errors);
        Assert.Throws<NotSupportedException>(() => collection.Clear());
        Assert.True(result.IsFailure);
        Assert.Equal(2, result.Errors.Count);
    }

    [Fact]
    public void Equals_TwoFailuresWithEqualErrors_AreEqual()
    {
        var first = Result<int>.Failure("x");
        var second = Result<int>.Failure("x");

        Assert.Equal(first, second);
        Assert.True(first == second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void Equals_TwoNonGenericFailuresWithEqualErrors_AreEqual()
    {
        var first = Result.Failure([Error.FromMessage("one"), Error.FromMessage("two")]);
        var second = Result.Failure(new List<Error> { Error.FromMessage("one"), Error.FromMessage("two") });

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void Equals_TwoSuccessesWithEqualValues_AreEqual()
    {
        var first = Result<string>.Success("v");
        var second = Result<string>.Success("v");

        // The payload arm of Result<TPayload>.GetHashCode is only reachable on a success, so a
        // success carrying a real value has to be hashed here or that arm stays untested.
        Assert.Equal(first, second);
        Assert.True(first == second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());

        Assert.Equal(Result.Success(), Result.Success());
        Assert.True(Result.Success() == Result.Success());
        Assert.Equal(Result.Success().GetHashCode(), Result.Success().GetHashCode());
    }

    [Fact]
    public void Equals_SuccessesWithDifferentValues_AreNotEqual()
    {
        Assert.NotEqual(Result<int>.Success(1), Result<int>.Success(2));
    }

    [Fact]
    public void Equals_FailuresWithDifferentErrorOrder_AreNotEqual()
    {
        var one = Error.FromMessage("one");
        var two = Error.FromMessage("two");

        Assert.NotEqual(Result.Failure([one, two]), Result.Failure([two, one]));
    }

    [Fact]
    public void Equals_ASuccessAndAFailure_AreNotEqual()
    {
        Assert.NotEqual(Result<int>.Success(0), Result<int>.Failure("x"));
        Assert.NotEqual(Result.Success(), Result.Failure("x"));
    }

    [Fact]
    public void Equals_WithNull_IsFalse()
    {
        Assert.False(Result<int>.Success(1).Equals(null));
        Assert.False(Result.Success().Equals(null));
    }
}
