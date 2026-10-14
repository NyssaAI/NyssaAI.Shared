namespace NyssaAI.Results.UseCaseTests.fixtures;

internal sealed class ImportWorkspace : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory(
        "nyssa-result-tutorial-"
    );

    public string DirectoryPath => _directory.FullName;
    public string InputPath => Path.Combine(DirectoryPath, "import.txt");

    public void Dispose() => _directory.Delete(recursive: true);
}
