using NyssaAI.Logging.FileLogging;

namespace NyssaAI.Logging.Tests;

internal sealed class TempLog : IDisposable
{
    public TempLog(
        long? rotationTriggerBytes = null,
        TimeProvider? timeProvider = null)
    {
        DirectoryPath = Path.Combine(Path.GetTempPath(), $"LoggingTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(DirectoryPath);
        try
        {
            FilePath = Path.Combine(DirectoryPath, "test.log");
            Log = rotationTriggerBytes is null
                ? new LogFile(FilePath, timeProvider)
                : new LogFile(FilePath, rotationTriggerBytes.Value, timeProvider);
        }
        catch
        {
            if (Directory.Exists(DirectoryPath))
            {
                Directory.Delete(DirectoryPath, recursive: true);
            }

            throw;
        }
    }

    public string DirectoryPath { get; }

    public string FilePath { get; }

    public LogFile Log { get; }

    public void Dispose()
    {
        Log.Dispose();
        if (Directory.Exists(DirectoryPath))
        {
            Directory.Delete(DirectoryPath, recursive: true);
        }
    }
}
