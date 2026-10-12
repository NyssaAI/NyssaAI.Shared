using NyssaAI.Results;

namespace NyssaAI.Configuration.UseCaseTests.domain;

/// <summary>
/// A class in SomeApp-cli that needs a setting. It takes <see cref="IConfiguration"/> in its
/// constructor and nothing else — no directory, no key, no provider — which is the whole of what
/// setup has to make true.
/// </summary>
public sealed class MailService
{
    private readonly IConfiguration _configuration;

    public MailService(IConfiguration configuration) => _configuration = configuration;

    public ValueTask<Result> RememberPasswordAsync(string password, CancellationToken cancellationToken) =>
        _configuration.SetAsync("mail", "password", password, provider: "aesLocal", cancellationToken: cancellationToken);

    public ValueTask<Result<string>> PasswordAsync(CancellationToken cancellationToken) =>
        _configuration.GetAsync("mail", "password", cancellationToken);
}
