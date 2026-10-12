using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NyssaAI.Configuration.Contracts;

namespace NyssaAI.Configuration.UseCaseTests.fixtures;

/// <summary>
/// What a vault package ships alongside its factory: one registration method in the fluent
/// convention, so the application writes <c>services.AddContosoVault()</c> and never names the
/// factory type. Here it takes the instance so a tutorial can look inside afterwards.
/// </summary>
public static class FakeVaultServiceCollectionExtensions
{
    public static IServiceCollection AddFakeVault(this IServiceCollection services, FakeVaultFactory factory)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ICloudKeyVaultFactory>(factory));
        return services;
    }
}
