using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace NyssaAI.Logging.UseCaseTests.fixtures;

/// <summary>Owns a validated host for the registrations shown by a logging tutorial.</summary>
public static class ServiceContainer
{
    public static IHost BuildValidated(IServiceCollection services)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();

        foreach (var descriptor in services)
        {
            builder.Services.Add(descriptor);
        }

        builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true,
        }));

        return builder.Build();
    }
}
