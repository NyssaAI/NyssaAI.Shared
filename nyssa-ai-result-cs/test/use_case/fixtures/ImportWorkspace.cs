namespace NyssaAI.Results.UseCaseTests.fixtures;

internal sealed class ImportWorkspace : IDisposable
{
    private readonly string _directoryPath;

    public ImportWorkspace()
    {
        var tempRoot = ResolveTempRoot();
        Directory.CreateDirectory(tempRoot);
        _directoryPath = Path.Combine(tempRoot, $"nyssa-result-tutorial-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directoryPath);
    }

    public string DirectoryPath => _directoryPath;
    public string InputPath => Path.Combine(DirectoryPath, "import.txt");

    public void Dispose()
    {
        if (Directory.Exists(_directoryPath))
        {
            try
            {
                Directory.Delete(_directoryPath, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup
            }
        }
    }

    private static string ResolveTempRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, ".temp");
            if (Directory.Exists(candidate) || File.Exists(Path.Combine(current.FullName, "NyssaAI.Shared.slnx")))
                return Path.Combine(current.FullName, ".temp");
            current = current.Parent;
        }

        return Path.Combine(Directory.GetCurrentDirectory(), ".temp");
    }
}
