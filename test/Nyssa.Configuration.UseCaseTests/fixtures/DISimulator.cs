using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace NyssaAI.Configuration.UseCaseTests.fixtures;

/// <summary>
/// Stands in for the host an application runs in: hand it the registrations <c>Program.cs</c> made
/// and it hands back what a class's constructor would have been given.
/// </summary>
/// <remarks>
/// <para>
/// Everything this library exposes arrives one way: a class declares <see cref="IConfiguration"/>
/// — or <see cref="IConfigurationExtensions"/> — as a constructor parameter, and the host supplies
/// it. There is no static accessor and no way to construct a client by hand, so a tutorial that
/// wants to show the developer's experience has to go through a container. This is that container,
/// with the ceremony behind one call.
/// </para>
/// <para>
/// The container is the generic host's, built by <see cref="ApplicationHost"/>, so what a tutorial
/// resolves is what a real application resolves. It holds one scope for its lifetime, because the
/// library's registrations are scoped — what it hands out is the set one scope builds, which is
/// what a single request, or a single CLI invocation, sees.
/// </para>
/// <para>
/// Resolution is also where the library does its work: the registration call only records the
/// startup options, and it is asking for a service that reads the manifest, creates the
/// configuration directory, and provisions the encryption key. So an unconfigured machine fails
/// here, not at registration.
/// </para>
/// </remarks>
public sealed class DISimulator : IDisposable
{
    private readonly IHost _application;
    private IServiceScope _scope;

    /// <summary>Builds the host the application registered, and opens the scope its work runs in.</summary>
    /// <param name="services">The registrations <c>Program.cs</c> made — see <see cref="Workstation.CreateServices"/>.</param>
    public DISimulator(IServiceCollection services)
    {
        _application = ApplicationHost.Build(services);
        _scope = _application.Services.CreateScope();
    }

    /// <summary>The <see cref="IConfiguration"/> a class declaring it as a constructor parameter is handed.</summary>
    public IConfiguration Configuration => Inject<IConfiguration>();

    /// <summary>The scoped provider owned by this fixture.</summary>
    public IServiceProvider Services => _scope.ServiceProvider;

    /// <summary>What a constructor declaring <typeparamref name="T"/> is handed.</summary>
    /// <typeparam name="T">The dependency the application's own class asks for.</typeparam>
    public T Inject<T>()
        where T : notnull =>
        _scope.ServiceProvider.GetRequiredService<T>();

    /// <summary>Begins a new caller scope while retaining the host and its shared services.</summary>
    public IConfiguration NextConfiguration()
    {
        _scope.Dispose();
        _scope = _application.Services.CreateScope();
        return Configuration;
    }

    /// <summary>Ends the scope and the host, as a host ends a request and then shuts down.</summary>
    public void Dispose()
    {
        _scope.Dispose();
        _application.Dispose();
    }
}
