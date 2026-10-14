using NyssaAI.Repository.Commands;
using Xunit;

namespace NyssaAI.Repository.Tests;

public sealed class RepoCommandKeyTests
{
    [Theory]
    [InlineData("tasks.list", "tasks.list", true)]
    [InlineData("tasks.list", "tasks.find", false)]
    [InlineData("tasks.list", "Tasks.List", false)]
    public void Equals_OperationNamesWithinOneContract_UsesRecordValueEquality(
        string firstName,
        string secondName,
        bool expected
    )
    {
        // Arrange
        var first = new RepoCommandKey<string, int> { Name = firstName };
        var second = new RepoCommandKey<string, int> { Name = secondName };

        // Act
        var equal = first.Equals(second);

        // Assert
        Assert.Equal(expected, equal);
    }

    [Fact]
    public void Equals_SameNameWithDifferentContracts_DoesNotConflateKeys()
    {
        // Arrange
        var count = new RepoCommandKey<string, int> { Name = "tasks.search" };
        var list = new RepoCommandKey<string, IReadOnlyList<string>> { Name = "tasks.search" };
        var otherInput = new RepoCommandKey<int, int> { Name = "tasks.search" };

        // Act
        var sameResult = count.Equals((object)list);
        var sameInput = count.Equals((object)otherInput);

        // Assert
        Assert.False(sameResult);
        Assert.False(sameInput);
    }
}
