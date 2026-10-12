using Xunit;

namespace NyssaAI.Results.Tests;

public sealed class GuardCollectionTests
{
    [Theory]
    [InlineData(true, 0, true)]
    [InlineData(true, 1, false)]
    [InlineData(true, 2, false)]
    [InlineData(false, 0, false)]
    [InlineData(false, 1, true)]
    [InlineData(false, 2, true)]
    public void ContainsGuards_Collection_RejectNamedState(bool contains, int count, bool succeeds)
    {
        List<int> values = Enumerable.Range(1, count).ToList();
        var result = contains
            ? Guard.Against.Contains(values, (int value) => value == 1, "Cannot continue", "Match rule", ErrorCategory.Conflict)
            : Guard.Against.NotContains(values, (int value) => value == 1, "Cannot continue", "Match rule", ErrorCategory.Conflict);

        // A successful result retains both the concrete collection type and the original instance.
        Result<List<int>> typed = result;
        Assert.Equal(succeeds, typed.IsSuccess);
        if (succeeds) Assert.Same(values, typed.ThrowIfError());
        else
        {
            var error = Assert.Single(typed.Errors);
            Assert.Equal(ErrorCategory.Conflict, error.Category);
            Assert.Equal("Cannot continue", error.UserMessage);
            Assert.Equal("Match rule", error.TechnicalMessage);
            Assert.Equal(typed.Errors, Assert.Throws<ResultException>(() => typed.ThrowIfError()).Errors);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ContainsGuards_NullCollection_ReturnValidationWithoutCallingPredicate(bool contains)
    {
        List<int>? values = null;
        var called = false;
        Func<int, bool> predicate = _ => { called = true; return true; };
        var result = contains
            ? Guard.Against.Contains(values, predicate, "Cannot continue", "Match rule", ErrorCategory.Conflict)
            : Guard.Against.NotContains(values, predicate, "Cannot continue", "Match rule", ErrorCategory.Conflict);

        Assert.False(called);
        var error = Assert.Single(result.Errors);
        Assert.Equal(ErrorCategory.Validation, error.Category);
        Assert.Equal("Cannot continue", error.UserMessage);
        Assert.Equal("The collection cannot be null.", error.TechnicalMessage);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ContainsGuards_NoMatch_InspectsEachItemOnceAndUsesDefaultMessage(bool contains)
    {
        var values = new List<int> { 2, 3 };
        var visited = new List<int>();
        Func<int, bool> predicate = value => { visited.Add(value); return value == 1; };
        var result = contains
            ? Guard.Against.Contains(values, predicate, "Invalid")
            : Guard.Against.NotContains(values, predicate, "Invalid");
        Assert.Equal(values, visited);
        Assert.Equal(contains, result.IsSuccess);
        if (contains) Assert.Same(values, result.Value);
        else
        {
            var error = Assert.Single(result.Errors);
            Assert.Equal(ErrorCategory.Validation, error.Category);
            Assert.Equal("Invalid", error.UserMessage);
            Assert.Equal("Invalid", error.TechnicalMessage);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ContainsGuards_NullPredicate_ReturnValidation(bool contains)
    {
        var values = new List<int>();
        Func<int, bool>? predicate = null;
        var result = contains
            ? Guard.Against.Contains(values, predicate, "Cannot continue")
            : Guard.Against.NotContains(values, predicate, "Cannot continue");
        Assert.Equal(ErrorCategory.Validation, Assert.Single(result.Errors).Category);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ContainsGuards_Match_StopsEnumerationAndDisposesEnumerator(bool contains)
    {
        var disposed = false;
        IEnumerable<int> Values()
        {
            try
            {
                yield return 1;
                throw new InvalidOperationException("Must stop after matching");
            }
            finally { disposed = true; }
        }

        var values = Values();
        var result = contains
            ? Guard.Against.Contains(values, (int value) => value == 1, "Invalid")
            : Guard.Against.NotContains(values, (int value) => value == 1, "Invalid");

        Assert.Equal(contains, result.IsFailure);
        Assert.True(disposed);
        if (contains) Assert.Equal(ErrorCategory.Validation, Assert.Single(result.Errors).Category);
        else Assert.Same(values, result.Value);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void ContainsGuards_ThrowingCallerCode_ReturnFailure(bool contains, bool predicateThrows)
    {
        IEnumerable<int> Values()
        {
            yield return 1;
            throw new InvalidOperationException("enumeration failed");
        }
        Func<int, bool> predicate = _ => predicateThrows ? throw new InvalidOperationException("predicate failed") : false;
        var values = Values();
        var result = contains
            ? Guard.Against.Contains(values, predicate, "Invalid")
            : Guard.Against.NotContains(values, predicate, "Invalid");
        var error = Assert.Single(result.Errors);
        Assert.Equal(ErrorCategory.Unexpected, error.Category);
        Assert.Contains(predicateThrows ? "predicate failed" : "enumeration failed", error.TechnicalMessage, StringComparison.Ordinal);
    }
}
