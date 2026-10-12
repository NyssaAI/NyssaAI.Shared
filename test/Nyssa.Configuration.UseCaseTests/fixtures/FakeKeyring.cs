using NyssaAI.Configuration.Contracts;
using NyssaAI.Results;

namespace NyssaAI.Configuration.UseCaseTests.fixtures;

/// <summary>An in-memory credential store, so a tutorial never touches the real one.</summary>
public sealed class FakeKeyring : IKeyring
{
    private readonly Dictionary<string, string> _secrets = new(StringComparer.Ordinal);

    /// <summary>Every Set the library made, in order — how a tutorial proves it wrote nothing.</summary>
    public List<(string Service, string Key)> Writes { get; } = [];

    /// <summary>When true, every call fails as a machine with no credential store would.</summary>
    public bool Unreachable { get; set; }

    public Result<string?> Get(string service, string key) =>
        Unreachable
            ? Result<string?>.Failure(Unavailable())
            : Result<string?>.Success(_secrets.GetValueOrDefault(Address(service, key)));

    public Result Set(string service, string key, string secret)
    {
        if (Unreachable)
            return Result.Failure(Unavailable());
        _secrets[Address(service, key)] = secret;
        Writes.Add((service, key));
        return Result.Success();
    }

    public Result Delete(string service, string key)
    {
        if (Unreachable)
            return Result.Failure(Unavailable());
        _secrets.Remove(Address(service, key));
        return Result.Success();
    }

    public bool Holds(string service, string key) => _secrets.ContainsKey(Address(service, key));

    private static string Address(string service, string key) => $"{service}/{key}";

    private static Error Unavailable() => Error.FromMessages(
        "The credential store is not available.",
        "keyring: no credential store on this machine",
        ErrorCategory.Unexpected);
}
