using System.Globalization;
using NyssaAI.Configuration.Client;
using NyssaAI.Configuration.CloudKeyVaults;
using NyssaAI.Configuration.Contracts;
using NyssaAI.Configuration.Recovery;
using NyssaAI.Results;

namespace NyssaAI.Configuration.UseCaseTests.fixtures;

/// <summary>A deterministic backend with durable request records, revisions, and atomic guarded execution.</summary>
public sealed class RecoveryVaultFactory : ICloudKeyVaultFactory
{
    public string Kind => "recovery-test";
    public OAuthOptions? Authorization(VaultOptions settings) => null;
    public string Identity { get; set; } = "test-account/test-vault";
    public RecoveryVault Backend { get; } = new();
    public Result<ICloudKeyVault> Create(VaultOptions settings, IAccessTokenSource? tokens) =>
        Result<ICloudKeyVault>.Success(new RecoveryVaultAdapter(Backend, settings.Name, Identity));
}

public sealed class RecoveryVault
{
    public object Gate { get; } = new();
    public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, int> Revisions { get; } = new(StringComparer.Ordinal);
    public Dictionary<Guid, RecoveryVaultReceipt> Requests { get; } = [];
    public int Mutations { get; set; }
    public int Reconciliations { get; set; }
    public string? LoseResponseForKey { get; set; }
    public string? FailBeforeKey { get; set; }
    public bool FailRemoval { get; set; }
    public bool Retain { get; set; }
    public bool EvidenceExpired { get; set; }
    public Action? AfterMutation { get; set; }

    public void Replace(string address, string value)
    {
        lock (Gate)
        {
            Values[address] = value;
            Revisions[address] = Revisions.GetValueOrDefault(address) + 1;
        }
    }
}

public sealed record RecoveryVaultReceipt
{
    public required CloudKeyVaultOperation Operation { get; init; }
    public required CloudKeyVaultPreparation Preparation { get; init; }
    public required CloudKeyVaultEffect Effect { get; init; }
    public required int Revision { get; init; }
}

public sealed class RecoveryVaultAdapter : ICloudKeyVault, ICloudKeyVaultRecovery
{
    private readonly RecoveryVault _backend;
    private readonly string _name;
    public RecoveryVaultAdapter(RecoveryVault backend, string name, string identity)
    {
        _backend = backend; _name = name; StoreIdentity = identity;
    }
    public string StoreIdentity { get; }
    public ValueTask<Result<string?>> ReadAsync(ConfigurationPointer pointer, CancellationToken cancellationToken = default)
    {
        lock (_backend.Gate) return ValueTask.FromResult(Result<string?>.Success(_backend.Values.GetValueOrDefault(pointer.Value)));
    }
    public ValueTask<Result<ConfigurationPointer>> WriteAsync(string @namespace, string key, string value, CancellationToken cancellationToken = default)
    {
        _backend.Replace($"{@namespace}/{key}", value);
        return ValueTask.FromResult(ConfigurationPointer.Parse(_name, $"{@namespace}/{key}"));
    }
    public ValueTask<Result<ConfigurationRemoval>> RemoveAsync(ConfigurationPointer pointer, CancellationToken cancellationToken = default)
    {
        lock (_backend.Gate)
        {
            if (!_backend.Retain) _backend.Values.Remove(pointer.Value);
            _backend.Revisions[pointer.Value] = _backend.Revisions.GetValueOrDefault(pointer.Value) + 1;
        }
        return ValueTask.FromResult(Result<ConfigurationRemoval>.Success(new ConfigurationRemoval { RemoteValueRemains = _backend.Retain }));
    }
    public ValueTask<Result<CloudKeyVaultPreparation>> PrepareAsync(CloudKeyVaultOperation operation, CancellationToken cancellationToken = default)
    {
        lock (_backend.Gate) return ValueTask.FromResult(Result<CloudKeyVaultPreparation>.Success(new CloudKeyVaultPreparation
        {
            StoreIdentity = StoreIdentity, TargetReference = Address(operation),
            Context = _backend.Revisions.GetValueOrDefault(Address(operation)).ToString(CultureInfo.InvariantCulture),
        }));
    }
    public ValueTask<Result<CloudKeyVaultEffect>> ExecuteAsync(CloudKeyVaultOperation operation, CloudKeyVaultPreparation preparation, CancellationToken cancellationToken = default)
    {
        lock (_backend.Gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var before = Inspect(operation, preparation);
            if (before.State != ConfigurationStepState.Failed) return Answer(before);
            if ((operation.Action == CloudKeyVaultAction.Remove && _backend.FailRemoval)
                || operation.Key == _backend.FailBeforeKey)
            {
                _backend.FailBeforeKey = null;
                return Answer(new CloudKeyVaultEffect { State = ConfigurationStepState.Failed });
            }
            var address = Address(operation);
            if (operation.Action == CloudKeyVaultAction.Write) _backend.Values[address] = operation.Value!;
            else if (!_backend.Retain) _backend.Values.Remove(address);
            var revision = _backend.Revisions.GetValueOrDefault(address) + 1;
            _backend.Revisions[address] = revision;
            _backend.Mutations++;
            var effect = new CloudKeyVaultEffect
            {
                State = operation.Action == CloudKeyVaultAction.Remove && _backend.Retain ? ConfigurationStepState.Retained : ConfigurationStepState.Applied,
                Pointer = operation.Action == CloudKeyVaultAction.Write ? ConfigurationPointer.Parse(_name, address).ThrowIfError() : null,
            };
            _backend.Requests.Add(operation.OperationId, new RecoveryVaultReceipt
            {
                Operation = operation, Preparation = preparation, Effect = effect, Revision = revision,
            });
            _backend.AfterMutation?.Invoke();
            if (operation.Key == _backend.LoseResponseForKey)
            {
                _backend.LoseResponseForKey = null;
                throw new TimeoutException("Response lost after committing.");
            }
            cancellationToken.ThrowIfCancellationRequested();
            return Answer(effect);
        }
    }
    public ValueTask<Result<CloudKeyVaultEffect>> ReconcileAsync(CloudKeyVaultOperation operation, CloudKeyVaultPreparation preparation, CancellationToken cancellationToken = default)
    {
        lock (_backend.Gate)
        {
            _backend.Reconciliations++;
            return Answer(Inspect(operation, preparation));
        }
    }
    private CloudKeyVaultEffect Inspect(CloudKeyVaultOperation operation, CloudKeyVaultPreparation preparation)
    {
        if (preparation.StoreIdentity != StoreIdentity) return new() { State = ConfigurationStepState.Conflict };
        if (_backend.EvidenceExpired) return new() { State = ConfigurationStepState.Manual };
        var revision = _backend.Revisions.GetValueOrDefault(Address(operation));
        if (_backend.Requests.TryGetValue(operation.OperationId, out var receipt))
        {
            if (receipt.Operation != operation || receipt.Preparation != preparation || receipt.Revision != revision)
                return new() { State = ConfigurationStepState.Conflict };
            return receipt.Effect;
        }
        return new() { State = revision.ToString(CultureInfo.InvariantCulture) == preparation.Context
            ? ConfigurationStepState.Failed : ConfigurationStepState.Conflict };
    }
    private static string Address(CloudKeyVaultOperation operation) => operation.Pointer?.Value ?? $"{operation.Namespace}/{operation.Key}";
    private static ValueTask<Result<CloudKeyVaultEffect>> Answer(CloudKeyVaultEffect effect) =>
        ValueTask.FromResult(Result<CloudKeyVaultEffect>.Success(effect));
}
