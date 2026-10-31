using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace NyssaAI.Configuration.UseCaseTests.fixtures;

/// <summary>
/// Builds the generic host an application runs in, from the registrations <c>Program.cs</c> made.
/// </summary>
/// <remarks>
/// A tutorial resolves what a host resolves, not what a hand-built container resolves: the same
/// scope rules, the same validation, and the host's own services present alongside the
/// application's. <see cref="HostBuilder"/> rather than <c>Host.CreateApplicationBuilder</c>,
/// because the defaults that builder adds — appsettings.json, environment variables, console
/// logging — would let the machine a test runs on change what the test sees.
/// </remarks>
internal static class ApplicationHost
{
    /// <summary>Builds the host over one set of registrations, with the scope checks a host applies in development.</summary>
    /// <param name="services">The registrations the tutorial made.</param>
    public static IHost Build(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return new HostBuilder()
            .UseDefaultServiceProvider(options =>
            {
                options.ValidateScopes = true;
                options.ValidateOnBuild = true;
            })
            .ConfigureServices((_, hosted) =>
            {
                foreach (var registration in services)
                    hosted.Add(registration);
            })
            .Build();
    }
}

/// <summary>Reaches the host's container the way a tutorial reads best.</summary>
internal static class HostScopeExtensions
{
    /// <summary>Opens the scope a request, or a single CLI invocation, runs in.</summary>
    /// <param name="host">The host the application is running in.</param>
    public static IServiceScope CreateScope(this IHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        return host.Services.CreateScope();
    }
}
