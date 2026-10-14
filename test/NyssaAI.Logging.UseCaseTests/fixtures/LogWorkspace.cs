using System.Text.Json;

namespace NyssaAI.Logging.UseCaseTests.fixtures;

/// <summary>
/// A temporary directory and log path for one workflow, deleted with the scope.
/// </summary>
/// <remarks>
/// One per test rather than one shared fixture: the provider holds the log file open for the life
/// of the container, so a workflow reads its own output only after disposing the container that
/// wrote it. Sharing a container between tests would mean one of them reading a file still open.
/// </remarks>
public sealed class LogWorkspace : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory(
        "nyssa-logging-usecase-"
    );
    public string LogPath => Path.Combine(_directory.FullName, "orders.jsonl");

    /// <summary>Read every JSON record written to the log file.</summary>
    public IReadOnlyList<JsonElement> ReadRecords() =>
        File.ReadAllLines(LogPath)
            .Select(line =>
            {
                using (var document = JsonDocument.Parse(line))
                {
                    return document.RootElement.Clone();
                }
            })
            .ToArray();

    public void Dispose() => _directory.Delete(recursive: true);
}
