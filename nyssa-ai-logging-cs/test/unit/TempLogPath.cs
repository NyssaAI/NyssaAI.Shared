namespace NyssaAI.Logging.Tests;

internal sealed class TempLogPath : IDisposable
{
    private readonly string _directory;

    public TempLogPath()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"LoggingTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
        LogPath = Path.Combine(_directory, "test.log");
    }

    public string LogPath { get; }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
