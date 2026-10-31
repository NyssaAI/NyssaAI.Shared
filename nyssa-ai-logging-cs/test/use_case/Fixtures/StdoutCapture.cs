namespace NyssaAI.Logging.UseCaseTests.Fixtures;

/// <summary>
/// Redirects <see cref="Console.Out"/> for the life of the scope and restores it on disposal.
/// </summary>
internal sealed class StdoutCapture : IDisposable
{
    private readonly TextWriter _previous = Console.Out;
    private readonly StringWriter _writer = new();

    public StdoutCapture() => Console.SetOut(_writer);

    public string Text => _writer.ToString();

    public void Dispose()
    {
        Console.SetOut(_previous);
        _writer.Dispose();
    }
}
