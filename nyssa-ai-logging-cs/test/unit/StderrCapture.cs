namespace NyssaAI.Logging.Tests;

/// <summary>
/// Redirects <see cref="Console.Error"/> for the life of the scope and restores it on disposal.
/// Console.Error is process-wide, so acquiring it and restoring it have to be the same scope: a
/// redirect made outside the block that restores it leaks a disposed writer into every later test
/// when the code between them throws.
/// </summary>
internal sealed class StderrCapture : IDisposable
{
    private readonly TextWriter _previous = Console.Error;
    private readonly StringWriter _writer = new();

    public StderrCapture() => Console.SetError(_writer);

    public string Text => _writer.ToString();

    public void Dispose()
    {
        Console.SetError(_previous);
        _writer.Dispose();
    }
}
