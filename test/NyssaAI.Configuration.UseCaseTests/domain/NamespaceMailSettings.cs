using System.Text.Json.Serialization;

namespace NyssaAI.Configuration.UseCaseTests.domain;

/// <summary>Mail settings assembled from independently stored namespace properties.</summary>
internal sealed record NamespaceMailSettings
{
    public required string Host { get; init; }
    public required int Port { get; init; }
    public required bool UseTls { get; init; }
    public required string Password { get; init; }
    [JsonPropertyName("api-key")]
    public required string ApiKey { get; init; }
    public string? Description { get; init; }
    public int[] RetryDelays { get; init; } = [];
}
