namespace NyssaAI.Configuration.Tests.Support;

internal sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory(string prefix = "nyssa-configuration-")
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            prefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        for (var attempt = 0; Directory.Exists(Path); attempt++)
        {
            try
            {
                Directory.Delete(Path, recursive: true);
                return;
            }
            catch (Exception exception) when (
                attempt < 10 && exception is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(20);
            }
        }
    }
}
