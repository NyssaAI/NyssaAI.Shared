using NyssaAI.Logging.FileLogging;
using System.Runtime.Versioning;
using Xunit;

namespace NyssaAI.Logging.Tests;

public sealed class LogFileTests
{
    [Fact]
    public void Initialize_WithAFreshPath_CreatesTheDirectory()
    {
        var scope = new TempLog();
        try
        {
            Assert.Null(scope.Log.Initialize());
            Assert.True(Directory.Exists(scope.DirectoryPath));
        }
        finally
        {
            scope.Dispose();
        }
    }

    [Fact]
    public void Initialize_WithAFreshPath_CreatesTheFile()
    {
        var scope = new TempLog();
        try
        {
            Assert.Null(scope.Log.Initialize());
            Assert.True(File.Exists(scope.FilePath));
        }
        finally
        {
            scope.Dispose();
        }
    }

    [Fact]
    public void Initialize_WithAPathThatHasNoDirectory_ReturnsAnError()
    {
        var logFile = new LogFile("test.log");

        Assert.NotNull(logFile.Initialize());
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public void Initialize_OnWindowsWhenACallerDirectoryAlreadyExists_KeepsItsInheritedPermissions()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows ACLs are only observable on Windows.");

        // The directory was created by the caller (TempLog pre-creates it), so the library
        // must not strip its inherited ACL: on a shared directory such as
        // C:\ProgramData\Logs that would lock every other service out.
        var scope = new TempLog();
        try
        {
            Assert.Null(scope.Log.Initialize());

            Assert.False(new DirectoryInfo(scope.DirectoryPath).GetAccessControl().AreAccessRulesProtected);
            Assert.True(new FileInfo(scope.FilePath).GetAccessControl().AreAccessRulesProtected);
        }
        finally
        {
            scope.Dispose();
        }
    }

    [Fact]
    [UnsupportedOSPlatform("windows")]
    public void Initialize_OnUnixWhenACallerDirectoryAlreadyExists_KeepsItsMode()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix file modes are only observable on Unix.");

        const UnixFileMode shared = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
            | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;

        var scope = new TempLog();
        try
        {
            File.SetUnixFileMode(scope.DirectoryPath, shared);

            Assert.Null(scope.Log.Initialize());

            Assert.Equal(shared, File.GetUnixFileMode(scope.DirectoryPath));
            Assert.Equal(OwnerOnlyAccess.OwnerReadWrite, File.GetUnixFileMode(scope.FilePath));
        }
        finally
        {
            scope.Dispose();
        }
    }

    [Fact]
    public void Initialize_WithASecondWriterOnTheSamePath_BothSucceedOnEveryPlatform()
    {
        // Two loggers with one name (two instances in a process, or two processes) must
        // behave the same on Windows as on Unix; the file is opened with write sharing.
        var scope = new TempLog();
        var second = new LogFile(scope.FilePath);
        try
        {
            Assert.Null(scope.Log.Initialize());
            Assert.Null(second.Initialize());

            scope.Log.WriteRecord(Record("from the first writer"));
            second.WriteRecord(Record("from the second writer"));
            scope.Log.Dispose();
            second.Dispose();

            var content = File.ReadAllText(scope.FilePath);
            Assert.Contains("from the first writer", content, StringComparison.Ordinal);
            Assert.Contains("from the second writer", content, StringComparison.Ordinal);
        }
        finally
        {
            second.Dispose();
            scope.Dispose();
        }
    }

    [Fact]
    public void Initialize_WithAnExpiredBackup_DeletesItAndKeepsAFreshOne()
    {
        // The age cutoff must run at startup: a low-volume consumer may never rotate again.
        var scope = new TempLog();
        var expired = $"{scope.FilePath}.2";
        var fresh = $"{scope.FilePath}.1";
        try
        {
            File.WriteAllText(expired, "old");
            File.SetLastWriteTimeUtc(expired, DateTime.UtcNow.AddDays(-(LogFile.RetentionDays + 1)));
            File.WriteAllText(fresh, "recent");

            Assert.Null(scope.Log.Initialize());

            Assert.False(File.Exists(expired));
            Assert.True(File.Exists(fresh));
        }
        finally
        {
            scope.Dispose();
        }
    }

    [Fact]
    public void WriteRecord_WithASimpleRecord_WritesOneJsonLine()
    {
        var scope = new TempLog();
        try
        {
            scope.Log.Initialize();
            scope.Log.WriteRecord(Record("Test message"));
            scope.Log.Dispose();

            var content = File.ReadAllText(scope.FilePath);
            Assert.Contains("Test message", content, StringComparison.Ordinal);
            Assert.Contains("Information", content, StringComparison.Ordinal);
            Assert.Single(content.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        }
        finally
        {
            scope.Dispose();
        }
    }

    [Fact]
    public void WriteRecord_WithStructuredFields_WritesThemIntoAdditionalFields()
    {
        var scope = new TempLog();
        try
        {
            scope.Log.Initialize();
            scope.Log.WriteRecord(Record("User action") with
            {
                AdditionalFields = new Dictionary<string, object?>
                {
                    ["userId"] = 123,
                    ["action"] = "login",
                },
            });
            scope.Log.Dispose();

            var content = File.ReadAllText(scope.FilePath);
            Assert.Contains("User action", content, StringComparison.Ordinal);
            Assert.Contains("userId", content, StringComparison.Ordinal);
            Assert.Contains("login", content, StringComparison.Ordinal);
        }
        finally
        {
            scope.Dispose();
        }
    }

    [Fact]
    public void WriteRecord_WithAnException_WritesTheExceptionText()
    {
        var scope = new TempLog();
        try
        {
            scope.Log.Initialize();
            scope.Log.WriteRecord(Record("Operation failed") with
            {
                Level = "Error",
                Exception = new InvalidOperationException("Test error").ToString(),
            });
            scope.Log.Dispose();

            var content = File.ReadAllText(scope.FilePath);
            Assert.Contains("Operation failed", content, StringComparison.Ordinal);
            Assert.Contains("InvalidOperationException", content, StringComparison.Ordinal);
        }
        finally
        {
            scope.Dispose();
        }
    }

    [Fact]
    public void WriteRecord_AfterDispose_IsIgnoredRatherThanThrowing()
    {
        var scope = new TempLog();
        try
        {
            scope.Log.Initialize();
            scope.Log.Dispose();

            scope.Log.WriteRecord(Record("after disposal"));

            Assert.DoesNotContain(
                "after disposal",
                File.ReadAllText(scope.FilePath),
                StringComparison.Ordinal);
        }
        finally
        {
            scope.Dispose();
        }
    }

    [Fact]
    public void Dispose_CalledTwice_ReleasesTheFileAndKeepsWhatWasWritten()
    {
        var scope = new TempLog();
        try
        {
            Assert.Null(scope.Log.Initialize());
            scope.Log.WriteRecord(Record("written before dispose"));

            scope.Log.Dispose();
            scope.Log.Dispose();

            // Asserts the effect rather than the absence of a throw: an assertion-free version of
            // this test passed against an empty Dispose body. On Windows a stream still open makes
            // the delete below fail, which is what "released" means here.
            Assert.Contains(
                "written before dispose",
                File.ReadAllText(scope.FilePath),
                StringComparison.Ordinal);
            File.Delete(scope.FilePath);
            Assert.False(File.Exists(scope.FilePath));
        }
        finally
        {
            scope.Dispose();
        }
    }

    private static LogRecord Record(string message) => new()
    {
        Timestamp = "2026-09-05T00:00:00.0000000Z",
        Level = "Information",
        Message = message,
        Module = "TestModule",
    };
}
