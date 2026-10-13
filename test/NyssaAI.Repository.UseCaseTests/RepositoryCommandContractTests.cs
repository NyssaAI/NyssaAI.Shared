using System.Reflection;
using System.Runtime.CompilerServices;
using NyssaAI.Repository.Commands;
using NyssaAI.Repository.Sessions;
using NyssaAI.Repository.Transactions;
using NyssaAI.Repository.UseCaseTests.domain;
using NyssaAI.Results;
using Xunit;

namespace NyssaAI.Repository.UseCaseTests;

// These checks inspect published contracts only, never private implementation state.
public sealed class RepositoryCommandContractTests
{
    [Theory]
    [InlineData("tasks.list", "tasks.list", true)]
    [InlineData("tasks.list", "tasks.find", false)]
    [InlineData("tasks.list", "Tasks.List", false)]
    public void Equals_OperationNamesWithinOneContract_UsesRecordValueEquality(
        string firstName, string secondName, bool expected)
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

    [Fact]
    public void RepoCommandKey_Name_IsRequiredAndInitOnly()
    {
        // Arrange
        var keyType = typeof(RepoCommandKey<string, int>);

        // Act
        var name = keyType.GetProperty(nameof(RepoCommandKey<string, int>.Name));
        var required = name?.GetCustomAttribute<RequiredMemberAttribute>();
        var setter = name?.SetMethod;
        var modifiers = setter?.ReturnParameter.GetRequiredCustomModifiers();

        // Assert
        Assert.NotNull(name);
        Assert.True(keyType.IsSealed);
        Assert.NotNull(required);
        Assert.NotNull(setter);
        Assert.NotNull(modifiers);
        Assert.Contains(typeof(IsExternalInit), modifiers);
    }

    [Fact]
    public void CommandRegistry_TypedKeyAndParameters_ReturnsTheMatchingCommandContract()
    {
        // Arrange
        var repositoryType = typeof(IRepository);

        // Act
        var registry = repositoryType.GetMethod(nameof(IRepository.CommandRegistry));
        var closed = registry?.MakeGenericMethod(typeof(string), typeof(IReadOnlyList<string>));

        // Assert
        Assert.NotNull(registry);
        Assert.True(registry.IsGenericMethodDefinition);
        Assert.NotNull(closed);
        Assert.Equal(typeof(Result<IRepoCommand<string, IReadOnlyList<string>>>), closed.ReturnType);
        Assert.Collection(closed.GetParameters(),
            key => Assert.Equal(typeof(RepoCommandKey<string, IReadOnlyList<string>>), key.ParameterType),
            parameters => Assert.Equal(typeof(string), parameters.ParameterType));
    }

    [Theory]
    [InlineData(typeof(int))]
    [InlineData(typeof(decimal))]
    [InlineData(typeof(string))]
    [InlineData(typeof(IReadOnlyList<string>))]
    [InlineData(typeof(CommandRow))]
    [InlineData(typeof(CommandReport))]
    public void ExecuteAsync_CompleteResultType_IsPreservedByTheSessionContract(Type resultType)
    {
        // Arrange
        var sessionType = typeof(IRepositorySession);

        // Act
        var execution = sessionType.GetMethod(nameof(IRepositorySession.ExecuteAsync));
        var closed = execution?.MakeGenericMethod(typeof(string), resultType);

        // Assert
        Assert.NotNull(execution);
        Assert.True(execution.IsGenericMethodDefinition);
        Assert.NotNull(closed);
        Assert.Equal(typeof(Task<>).MakeGenericType(typeof(Result<>).MakeGenericType(resultType)), closed.ReturnType);
        Assert.Collection(closed.GetParameters(),
            command => Assert.Equal(typeof(IRepoCommand<,>).MakeGenericType(typeof(string), resultType), command.ParameterType),
            cancellation =>
            {
                Assert.Equal(typeof(CancellationToken), cancellation.ParameterType);
                Assert.True(cancellation.IsOptional);
            });
    }

    [Fact]
    public void IRepoCommand_PublicSurface_CarriesTypedDataWithoutAnExecutionContext()
    {
        // Arrange
        var contract = typeof(IRepoCommand<string, int>);

        // Act
        var key = contract.GetProperty("Key");
        var parameters = contract.GetProperty("Parameters");
        var properties = contract.GetProperties();
        var commandText = contract.GetProperty("CommandText");
        var methods = contract.GetMethods().Where(method => !method.IsSpecialName).ToArray();

        // Assert
        Assert.Equal(typeof(RepoCommandKey<string, int>), key?.PropertyType);
        Assert.Equal(typeof(string), parameters?.PropertyType);
        Assert.All(properties, property => Assert.Null(property.SetMethod));
        Assert.Null(commandText);
        var execution = Assert.Single(methods);
        Assert.Equal("ExecuteCoreAsync", execution.Name);
        Assert.Equal(typeof(Task<Result<int>>), execution.ReturnType);
        Assert.Collection(execution.GetParameters(),
            connection => Assert.Equal(typeof(System.Data.Common.DbConnection), connection.ParameterType),
            transaction => Assert.Equal(typeof(System.Data.Common.DbTransaction), transaction.ParameterType),
            cancellation => Assert.Equal(typeof(CancellationToken), cancellation.ParameterType));
    }

    [Fact]
    public void IRepositoryTransaction_PublicSurface_InheritsTypedExecutionAndKeepsExplicitCompletion()
    {
        // Arrange
        var transactionType = typeof(IRepositoryTransaction);
        var sessionType = typeof(IRepositorySession);

        // Act
        var transactionInterfaces = transactionType.GetInterfaces();
        var sessionInterfaces = sessionType.GetInterfaces();
        var commit = transactionType.GetMethod(nameof(IRepositoryTransaction.CommitAsync));
        var rollback = transactionType.GetMethod(nameof(IRepositoryTransaction.RollbackAsync));
        var members = sessionType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        // Assert
        Assert.Contains(typeof(IRepositorySession), transactionInterfaces);
        Assert.Contains(typeof(IAsyncDisposable), sessionInterfaces);
        Assert.Equal(typeof(Task<Result>), commit?.ReturnType);
        Assert.Equal(typeof(Task<Result>), rollback?.ReturnType);
        Assert.Equal(nameof(IRepositorySession.ExecuteAsync), Assert.Single(members).Name);
    }

    [Fact]
    public void IRepository_OpeningContracts_ReturnCallerOwnedScopes()
    {
        // Arrange
        var repositoryType = typeof(IRepository);

        // Act
        var session = repositoryType.GetMethod(nameof(IRepository.OpenSessionAsync));
        var transaction = repositoryType.GetMethod(nameof(IRepository.OpenTransactionAsync));

        // Assert
        Assert.Equal(typeof(Task<Result<IRepositorySession>>), session?.ReturnType);
        Assert.Equal(typeof(Task<Result<IRepositoryTransaction>>), transaction?.ReturnType);
    }
}
