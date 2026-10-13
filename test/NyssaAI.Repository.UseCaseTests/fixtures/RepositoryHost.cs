using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace NyssaAI.Repository.UseCaseTests.fixtures;

/// <summary>Owns standard hosting while tutorials supply their public registration choices.</summary>
public sealed class RepositoryHost : IDisposable
{
    private IHost? _host;

    public void Register(Action<IServiceCollection> register)
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        register(builder.Services);
        _host = builder.Build();
    }

    public async Task RegisterAsync(Func<IServiceCollection, Task> register)
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        await register(builder.Services);
        _host = builder.Build();
    }

    public IServiceProvider Host => _host!.Services;

    public void Dispose() => _host?.Dispose();
}
