using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NyssaAI.Configuration.Client;
using NyssaAI.Configuration.Contracts;
using NyssaAI.Results;
using ConfigurationStore = NyssaAI.Configuration.IConfiguration;

namespace NyssaAI.Repository.UseCaseTests.fixtures;

// Owns isolated configuration hosts and their scopes, using only public registration APIs.
internal sealed class ConfigurationHost : IDisposable
{
    private readonly List<IDisposable> _owned = [];
    private readonly string _key;

    internal ConfigurationHost(string? encodedKey = null) =>
        _key = encodedKey ?? Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    internal ConfigurationStore Open(string directory, bool differentKey = false, ICloudKeyVaultFactory? vault = null)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton<IKeyring>(new MemoryKeyring(differentKey
            ? Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) : _key));
        if (vault is not null)
            builder.Services.AddSingleton(vault);

        builder.Services.AddNyssaConfiguration(options =>
        {
            options.ApplicationName = "repository-tests";
            options.ConfigDirectory = Path.Join(directory, "config");
            options.Bootstrap.ConfigManifest = Path.Join(directory, "home", ".config", "config-manifest.json");
            if (vault is not null)
                options.Bootstrap.KeyVault = new VaultOptions { Name = vault.Kind, Kind = vault.Kind };
        });

        var host = builder.Build();
        _owned.Add(host);
        var scope = host.Services.CreateScope();
        _owned.Add(scope);
        return scope.ServiceProvider.GetRequiredService<ConfigurationStore>();
    }

    public void Dispose()
    {
        for (var index = _owned.Count - 1; index >= 0; index--)
            _owned[index].Dispose();
        _owned.Clear();
    }

    private sealed class MemoryKeyring : IKeyring
    {
        private readonly Dictionary<string, string> _values = [];
        internal MemoryKeyring(string key) => _values["repository-tests/configuration-key"] = key;
        public Result<string?> Get(string service, string key) => Result<string?>.Success(_values.GetValueOrDefault($"{service}/{key}"));
        public Result Set(string service, string key, string secret)
        {
            _values[$"{service}/{key}"] = secret;
            return Result.Success();
        }
        public Result Delete(string service, string key)
        {
            _values.Remove($"{service}/{key}");
            return Result.Success();
        }
    }
}
