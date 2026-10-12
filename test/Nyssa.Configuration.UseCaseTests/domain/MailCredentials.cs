namespace NyssaAI.Configuration.UseCaseTests.domain;

/// <summary>The third level of the mail settings hierarchy, containing fictional tutorial credentials.</summary>
internal sealed record MailCredentials
{
    public required string UserName { get; init; }
    public required string Password { get; init; }
}
