namespace NyssaAI.Configuration.UseCaseTests.domain;

/// <summary>The transport protection and credentials used by the mail server.</summary>
internal sealed record MailSecuritySettings
{
    public required bool IsTlsEnabled { get; init; }
    public required MailCredentials Credentials { get; init; }
}
