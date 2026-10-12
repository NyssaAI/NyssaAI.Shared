namespace NyssaAI.Configuration.UseCaseTests.domain;

/// <summary>The application's mail server configuration, with security settings nested beneath it.</summary>
internal sealed record MailServerSettings
{
    public required string Host { get; init; }
    public required int Port { get; init; }
    public required MailSecuritySettings Security { get; init; }
}
