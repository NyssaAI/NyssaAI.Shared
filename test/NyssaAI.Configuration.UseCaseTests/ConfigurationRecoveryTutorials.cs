using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NyssaAI.Configuration.Client;
using NyssaAI.Configuration.CloudKeyVaults;
using NyssaAI.Configuration.Contracts;
using NyssaAI.Configuration.Recovery;
using NyssaAI.Configuration.UseCaseTests.domain;
using NyssaAI.Configuration.UseCaseTests.fixtures;
using NyssaAI.Results;
using Xunit;

namespace NyssaAI.Configuration.UseCaseTests;

/// <summary>Application-owned recovery through ordinary configuration calls and a consumer-implemented vault.</summary>
public sealed class ConfigurationRecoveryTutorials
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MoveAsync_UnfinishedCleanup_ResumesOriginalSourceAcrossRestartAsync(bool retain)
    {
        // Arrange
        using (var fixture = new Workstation())
        {
            var factory = new RecoveryVaultFactory();
            var token = TestContext.Current.CancellationToken;
            var services1 = fixture.CreateServices();
            services1.AddSingleton<ICloudKeyVaultFactory>(factory);
            services1.AddNyssaConfiguration(options =>
            {
                fixture.Locate(options);
                options.Bootstrap.KeyVault = new VaultOptions { Name = "recoverable", Kind = factory.Kind };
            });
            fixture.StartApplication(services1);
            var configuration = fixture.Host.GetRequiredService<IConfiguration>();

            // Act
            using (var nested = JsonDocument.Parse("""{"Flags":[true,false],"Options":{"Limit":4}}"""))
            {
                await configuration.SetAsAsync(
                    "mail",
                    "nested",
                    nested.RootElement,
                    RecoveryTutorialJsonContext.Default.JsonElement,
                    cancellationToken: token
                );
                await configuration.SetAsync("mail", "password", "fictional-secret", "recoverable", token);
                factory.Backend.FailRemoval = true;
                var moved = await configuration.MoveAsync("mail", "password", "aesLocal", token);
                var saved = Assert.IsType<ConfigurationRecoveryHandle>(moved.RecoveryHandle).Export();
                fixture.StopApplication();
                factory.Backend.FailRemoval = false;
                factory.Backend.Retain = retain;
                var services2 = fixture.CreateServices();
                services2.AddSingleton<ICloudKeyVaultFactory>(factory);
                services2.AddNyssaConfiguration(options =>
                {
                    fixture.Locate(options);
                    options.Bootstrap.KeyVault = new VaultOptions { Name = "recoverable", Kind = factory.Kind };
                });
                fixture.StartApplication(services2);
                configuration = fixture.Host.GetRequiredService<IConfiguration>();
                ConfigurationRecoveryOutcome recovered = await configuration.RecoverAsync(
                    ConfigurationRecoveryHandle.Import(saved),
                    token
                );
                var value = await configuration.GetAsync("mail", "password", token);
                var repeated = await configuration.RecoverAsync(ConfigurationRecoveryHandle.Import(saved), token);

                // Assert
                Assert.False(moved.SourceWithdrawn);
                ConfigurationOperationProgress progress = Assert.IsType<ConfigurationOperationProgress>(moved.Progress);
                ConfigurationStepProgress cleanup = progress.Steps.Single(step =>
                    step.Action == CloudKeyVaultAction.Remove
                );
                Assert.Equal(ConfigurationStepState.Failed, cleanup.State);
                Assert.Equal(ConfigurationPublicationState.Published, progress.Publication);
                Assert.Equal(ConfigurationOperationKind.Move, progress.Kind);
                Assert.Equal(ConfigurationRecoveryStatus.Completed, recovered.Status);
                Assert.Equal(
                    retain ? ConfigurationStepState.Retained : ConfigurationStepState.Applied,
                    recovered.Progress.Steps.Last().State
                );
                Assert.Equal(ConfigurationRecoveryStatus.Completed, repeated.Status);
                Assert.Equal("fictional-secret", value);
                Assert.Equal(1, factory.Backend.Mutations);
                Assert.Equal(retain, factory.Backend.Values.ContainsKey("mail/password"));
                if (retain)
                    Assert.Equal("fictional-secret", factory.Backend.Values["mail/password"]);
                Assert.Null(recovered.RecoveryHandle);
            }
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task NamespaceWrite_LostAcknowledgement_ResumesFrozenIntentWithoutDuplicateWritesAsync(
        bool merge,
        bool generated
    )
    {
        // Arrange
        using (var fixture = new Workstation())
        {
            var factory = new RecoveryVaultFactory();
            var token = TestContext.Current.CancellationToken;
            var services3 = fixture.CreateServices();
            services3.AddSingleton<ICloudKeyVaultFactory>(factory);
            services3.AddNyssaConfiguration(options =>
            {
                fixture.Locate(options);
                options.Bootstrap.KeyVault = new VaultOptions { Name = "recoverable", Kind = factory.Kind };
            });
            fixture.StartApplication(services3);
            var configuration = fixture.Host.GetRequiredService<IConfiguration>();
            using (var nested = JsonDocument.Parse("""{"Flags":[true,false],"Options":{"Limit":4}}"""))
            {
                await configuration.SetAsAsync(
                    "mail",
                    "nested",
                    nested.RootElement,
                    RecoveryTutorialJsonContext.Default.JsonElement,
                    cancellationToken: token
                );
                foreach (var key in new[] { "A", "B", "C" })
                    await configuration.SetAsync("mail", key, "old", "recoverable", token);
                await configuration.SetAsync("mail", "omitted", "keep-on-merge", cancellationToken: token);
                var before = fixture.ReadDocument("mail");
                using (var dto = JsonDocument.Parse("""{"A":"one","B":"two","C":"three"}"""))
                {
                    factory.Backend.LoseResponseForKey = "B";

                    // Act
                    var failure = await Assert.ThrowsAsync<ConfigurationOperationException>(async () =>
                    {
                        if (merge && generated)
                            await configuration.MergeSettingsAsync(
                                "mail",
                                RecoveryTutorialJsonContext.Default.JsonElement,
                                dto.RootElement,
                                token
                            );
                        else if (merge)
                            await configuration.MergeSettingsAsync("mail", dto.RootElement, token);
                        else if (generated)
                            await configuration.SetAsAsync(
                                "mail",
                                RecoveryTutorialJsonContext.Default.JsonElement,
                                dto.RootElement,
                                token
                            );
                        else
                            await configuration.SetAsAsync("mail", dto.RootElement, token);
                    });
                    var unchanged = fixture.ReadDocument("mail");
                    var saved = Assert.IsType<ConfigurationRecoveryHandle>(failure.RecoveryHandle).Export();
                    fixture.StopApplication();
                    var services4 = fixture.CreateServices();
                    services4.AddSingleton<ICloudKeyVaultFactory>(factory);
                    services4.AddNyssaConfiguration(options =>
                    {
                        fixture.Locate(options);
                        options.Bootstrap.KeyVault = new VaultOptions { Name = "recoverable", Kind = factory.Kind };
                    });
                    fixture.StartApplication(services4);
                    configuration = fixture.Host.GetRequiredService<IConfiguration>();
                    var recovered = await configuration.RecoverAsync(ConfigurationRecoveryHandle.Import(saved), token);
                    var repeated = await configuration.RecoverAsync(ConfigurationRecoveryHandle.Import(saved), token);
                    var keys = await configuration.ListAsync("mail", token);

                    // Assert
                    Assert.IsAssignableFrom<ResultException>(failure);
                    Assert.Equal(before, unchanged);
                    Assert.Equal(
                        new[]
                        {
                            ConfigurationStepState.Applied,
                            ConfigurationStepState.Unknown,
                            ConfigurationStepState.Pending,
                        },
                        failure.Progress.Steps.Select(step => step.State)
                    );
                    Assert.Equal(ConfigurationPublicationState.NotAttempted, failure.Progress.Publication);
                    Assert.Equal(ConfigurationRecoveryStatus.Completed, recovered.Status);
                    Assert.Equal(ConfigurationRecoveryStatus.Completed, repeated.Status);
                    Assert.Equal(3, factory.Backend.Mutations);
                    Assert.Equal("three", await configuration.GetAsync("mail", "C", token));
                    Assert.Equal(merge, keys.Contains("omitted"));
                    Assert.Equal(merge, keys.Contains("nested"));
                    Assert.DoesNotContain(saved, failure.ToString());
                    Assert.DoesNotContain("two", failure.ToString());
                    Assert.DoesNotContain(saved, ConfigurationRecoveryHandle.Import(saved).ToString());
                }
            }
        }
    }

    [Theory]
    [InlineData("local")]
    [InlineData("local-json")]
    [InlineData("remote")]
    [InlineData("provider")]
    [InlineData("expired")]
    public async Task RecoverAsync_ChangedEvidence_DoesNotPerformPendingMutationAsync(string change)
    {
        // Arrange
        using (var fixture = new Workstation())
        {
            var factory = new RecoveryVaultFactory();
            var token = TestContext.Current.CancellationToken;
            var services5 = fixture.CreateServices();
            services5.AddSingleton<ICloudKeyVaultFactory>(factory);
            services5.AddNyssaConfiguration(options =>
            {
                fixture.Locate(options);
                options.Bootstrap.KeyVault = new VaultOptions { Name = "recoverable", Kind = factory.Kind };
            });
            fixture.StartApplication(services5);
            var configuration = fixture.Host.GetRequiredService<IConfiguration>();
            using (var nested = JsonDocument.Parse("""{"Flags":[true,false],"Options":{"Limit":4}}"""))
            {
                await configuration.SetAsAsync(
                    "mail",
                    "nested",
                    nested.RootElement,
                    RecoveryTutorialJsonContext.Default.JsonElement,
                    cancellationToken: token
                );
                await configuration.SetAsync("mail", "A", "old", "recoverable", token);
                await configuration.SetAsync("mail", "B", "old", "recoverable", token);
                factory.Backend.FailBeforeKey = "B";
                using (var dto = JsonDocument.Parse("""{"A":"one","B":"two"}"""))
                {
                    var failure = await Assert.ThrowsAsync<ConfigurationOperationException>(() =>
                        configuration.SetAsAsync("mail", dto.RootElement, token).AsTask()
                    );

                    // Act
                    if (change == "local")
                        await configuration.SetAsync("mail", "newer", "keep", cancellationToken: token);
                    if (change == "local-json")
                    {
                        using (var newer = JsonDocument.Parse("""{"Flags":[false,false],"Options":{"Limit":4}}"""))
                        {
                            await configuration.SetAsAsync(
                                "mail",
                                "nested",
                                newer.RootElement,
                                RecoveryTutorialJsonContext.Default.JsonElement,
                                cancellationToken: token
                            );
                        }
                    }

                    if (change == "remote")
                        factory.Backend.Replace("mail/A", "newer");
                    if (change == "expired")
                        factory.Backend.EvidenceExpired = true;
                    if (change == "provider")
                    {
                        factory.Identity = "different-backend";
                        configuration = fixture.RefreshedConfigurationService();
                    }

                    var before = fixture.ReadDocument("mail");
                    var mutations = factory.Backend.Mutations;
                    var recovered = await configuration.RecoverAsync(failure.RecoveryHandle!, token);

                    // Assert
                    Assert.Equal(
                        change == "expired"
                            ? ConfigurationRecoveryStatus.ManualReconciliation
                            : ConfigurationRecoveryStatus.Conflict,
                        recovered.Status
                    );
                    Assert.Equal(mutations, factory.Backend.Mutations);
                    Assert.Equal(before, fixture.ReadDocument("mail"));
                    Assert.Equal("old", factory.Backend.Values["mail/B"]);
                    Assert.Null(recovered.RecoveryHandle);
                }
            }
        }
    }

    [Fact]
    public async Task RecoverAsync_SourceReplaced_PreservesNewerRemoteValueAsync()
    {
        // Arrange
        using (var fixture = new Workstation())
        {
            var factory = new RecoveryVaultFactory();
            var token = TestContext.Current.CancellationToken;
            var services6 = fixture.CreateServices();
            services6.AddSingleton<ICloudKeyVaultFactory>(factory);
            services6.AddNyssaConfiguration(options =>
            {
                fixture.Locate(options);
                options.Bootstrap.KeyVault = new VaultOptions { Name = "recoverable", Kind = factory.Kind };
            });
            fixture.StartApplication(services6);
            var configuration = fixture.Host.GetRequiredService<IConfiguration>();
            await configuration.SetAsync("mail", "password", "original", "recoverable", token);
            factory.Backend.FailRemoval = true;
            var moved = await configuration.MoveAsync("mail", "password", cancellationToken: token);

            // Act
            factory.Backend.Replace("mail/password", "newer");
            factory.Backend.FailRemoval = false;
            var recovered = await configuration.RecoverAsync(moved.RecoveryHandle!, token);

            // Assert
            Assert.Equal(ConfigurationRecoveryStatus.Conflict, recovered.Status);
            Assert.Equal("newer", factory.Backend.Values["mail/password"]);
            Assert.Equal("original", await configuration.GetAsync("mail", "password", token));
            Assert.Equal(0, factory.Backend.Mutations);
        }
    }

    [Fact]
    public async Task NamespaceWrite_CanceledAfterRemoteCommit_PreservesReceiptAndCallerTokenAsync()
    {
        // Arrange
        using (var fixture = new Workstation())
        {
            var factory = new RecoveryVaultFactory();
            var token = TestContext.Current.CancellationToken;
            using (var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                var services7 = fixture.CreateServices();
                services7.AddSingleton<ICloudKeyVaultFactory>(factory);
                services7.AddNyssaConfiguration(options =>
                {
                    fixture.Locate(options);
                    options.Bootstrap.KeyVault = new VaultOptions { Name = "recoverable", Kind = factory.Kind };
                });
                fixture.StartApplication(services7);
                var configuration = fixture.Host.GetRequiredService<IConfiguration>();
                await configuration.SetAsync("mail", "A", "old", "recoverable", token);
                factory.Backend.AfterMutation = cancellation.Cancel;
                using (var dto = JsonDocument.Parse("""{"A":"new","B":"later"}"""))
                {
                    // Act
                    var canceled = await Assert.ThrowsAsync<ConfigurationOperationCanceledException>(() =>
                        configuration.SetAsAsync("mail", dto.RootElement, cancellation.Token).AsTask()
                    );
                    factory.Backend.AfterMutation = null;
                    var recovered = await configuration.RecoverAsync(canceled.RecoveryHandle!, token);

                    // Assert
                    Assert.Equal(cancellation.Token, canceled.CancellationToken);
                    Assert.Equal(ConfigurationStepState.Unknown, canceled.Progress.Steps[0].State);
                    Assert.Equal(ConfigurationRecoveryStatus.Completed, recovered.Status);
                    Assert.Equal(1, factory.Backend.Mutations);
                    Assert.Equal("later", await configuration.GetAsync("mail", "B", token));
                }
            }
        }
    }

    [Fact]
    public async Task MoveAsync_LegacyAdapterFailure_ReportsManualReconciliationAsync()
    {
        // Arrange
        var vaults = new FakeVaultFactory();
        using (var fixture = new Workstation { VaultPackage = vaults })
        {
            var token = TestContext.Current.CancellationToken;
            var services = fixture.CreateServices();
            services.AddNyssaConfiguration(options =>
            {
                fixture.Locate(options);
                options.Bootstrap.KeyVault = new VaultOptions { Name = "legacy", Kind = "fake" };
            });
            fixture.StartApplication(services);
            var configuration = fixture.Host.GetRequiredService<IConfiguration>();
            await configuration.SetAsync("mail", "password", "secret", "legacy", token);
            vaults.Created[0].RemovalFailure = Error.FromMessage("Unavailable", ErrorCategory.Unavailable);

            // Act
            var moved = await configuration.MoveAsync("mail", "password", cancellationToken: token);

            // Assert
            Assert.Equal(ConfigurationRecoveryStatus.ManualReconciliation, moved.Progress!.Recovery);
            Assert.Equal(ConfigurationStepState.Unknown, moved.Progress.Steps.Last().State);
            Assert.Equal("mail/password", moved.Progress.Steps.Last().TargetReference);
            Assert.Null(moved.RecoveryHandle);
            Assert.Equal("secret", await configuration.GetAsync("mail", "password", token));
        }
    }

    [Fact]
    public async Task MoveAsync_DestinationResponseLost_ReusesOriginalWriteAsync()
    {
        // Arrange
        using (var fixture = new Workstation())
        {
            var factory = new RecoveryVaultFactory();
            var token = TestContext.Current.CancellationToken;
            var services8 = fixture.CreateServices();
            services8.AddSingleton<ICloudKeyVaultFactory>(factory);
            services8.AddNyssaConfiguration(options =>
            {
                fixture.Locate(options);
                options.Bootstrap.KeyVault = new VaultOptions { Name = "recoverable", Kind = factory.Kind };
            });
            fixture.StartApplication(services8);
            var configuration = fixture.Host.GetRequiredService<IConfiguration>();
            await configuration.SetAsync("mail", "password", "original", cancellationToken: token);
            var before = fixture.ReadDocument("mail");
            factory.Backend.LoseResponseForKey = "password";

            // Act
            var failure = await Assert.ThrowsAsync<ConfigurationOperationException>(() =>
                configuration.MoveAsync("mail", "password", "recoverable", token).AsTask()
            );
            Assert.Equal(before, fixture.ReadDocument("mail"));
            var recovered = await configuration.RecoverAsync(failure.RecoveryHandle!, token);
            var repeated = await configuration.RecoverAsync(failure.RecoveryHandle!, token);

            // Assert
            Assert.Equal(ConfigurationStepState.Unknown, failure.Progress.Steps[0].State);
            Assert.Equal(ConfigurationRecoveryStatus.Completed, recovered.Status);
            Assert.Equal(ConfigurationRecoveryStatus.Completed, repeated.Status);
            Assert.Equal("original", await configuration.GetAsync("mail", "password", token));
            Assert.Equal(1, factory.Backend.Mutations);
            Assert.Equal(failure.Progress.Steps[0].OperationId, Assert.Single(factory.Backend.Requests).Key);
        }
    }

    [Fact]
    public async Task RecoverAsync_ConcurrentScopes_ExecuteEachOriginalRequestOnceAsync()
    {
        // Arrange
        using (var fixture = new Workstation())
        {
            var factory = new RecoveryVaultFactory();
            var token = TestContext.Current.CancellationToken;
            var services9 = fixture.CreateServices();
            services9.AddSingleton<ICloudKeyVaultFactory>(factory);
            services9.AddNyssaConfiguration(options =>
            {
                fixture.Locate(options);
                options.Bootstrap.KeyVault = new VaultOptions { Name = "recoverable", Kind = factory.Kind };
            });
            fixture.StartApplication(services9);
            var configuration = fixture.Host.GetRequiredService<IConfiguration>();
            await configuration.SetAsync("mail", "A", "old", "recoverable", token);
            await configuration.SetAsync("mail", "B", "old", "recoverable", token);
            factory.Backend.FailBeforeKey = "B";
            using (var dto = JsonDocument.Parse("""{"A":"one","B":"two"}"""))
            {
                var failure = await Assert.ThrowsAsync<ConfigurationOperationException>(() =>
                    configuration.SetAsAsync("mail", dto.RootElement, token).AsTask()
                );
                var saved = failure.RecoveryHandle!.Export();
                var callers = Enumerable.Range(0, 8).Select(_ => fixture.RefreshedConfigurationService()).ToArray();
                var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

                // Act
                var pending = callers
                    .Select(async caller =>
                    {
                        await start.Task;
                        return await caller.RecoverAsync(ConfigurationRecoveryHandle.Import(saved), token);
                    })
                    .ToArray();
                start.SetResult();
                var outcomes = await Task.WhenAll(pending);

                // Assert
                Assert.All(outcomes, outcome => Assert.Equal(ConfigurationRecoveryStatus.Completed, outcome.Status));
                Assert.Equal(2, factory.Backend.Mutations);
                Assert.Equal(
                    failure.Progress.Steps.Select(step => step.OperationId).Order(),
                    factory.Backend.Requests.Keys.Order()
                );
                Assert.Equal("one", await configuration.GetAsync("mail", "A", token));
                Assert.Equal("two", await configuration.GetAsync("mail", "B", token));
            }
        }
    }
}
