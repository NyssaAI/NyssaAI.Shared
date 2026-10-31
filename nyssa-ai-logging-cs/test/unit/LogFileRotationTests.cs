using NyssaAI.Logging.FileLogging;
using Xunit;

namespace NyssaAI.Logging.Tests;

public sealed class LogFileRotationTests
{
    [Fact]
    public void WriteRecord_WhenTheFilePassesTheTrigger_ArchivesItAndStartsANewOne()
    {
        var scope = new TempLog(rotationTriggerBytes: 64);
        try
        {
            scope.Log.Initialize();
            scope.Log.WriteRecord(Record("first"));
            scope.Log.WriteRecord(Record("second"));
            scope.Log.Dispose();

            Assert.True(File.Exists($"{scope.FilePath}.1"), "the archived file should exist");
            Assert.Contains("first", File.ReadAllText($"{scope.FilePath}.1"), StringComparison.Ordinal);
            Assert.Contains("second", File.ReadAllText(scope.FilePath), StringComparison.Ordinal);
            Assert.DoesNotContain("first", File.ReadAllText(scope.FilePath), StringComparison.Ordinal);
        }
        finally
        {
            scope.Dispose();
        }
    }

    [Fact]
    public void WriteRecord_WhenRotatingRepeatedly_ShiftsBackupsOldestLast()
    {
        var scope = new TempLog(rotationTriggerBytes: 64);
        try
        {
            scope.Log.Initialize();
            scope.Log.WriteRecord(Record("oldest"));
            scope.Log.WriteRecord(Record("middle"));
            scope.Log.WriteRecord(Record("newest"));
            scope.Log.Dispose();

            Assert.Contains("oldest", File.ReadAllText($"{scope.FilePath}.2"), StringComparison.Ordinal);
            Assert.Contains("middle", File.ReadAllText($"{scope.FilePath}.1"), StringComparison.Ordinal);
            Assert.Contains("newest", File.ReadAllText(scope.FilePath), StringComparison.Ordinal);
        }
        finally
        {
            scope.Dispose();
        }
    }

    [Theory]
    // One record is larger than the 64-byte trigger, so every write after the first rotates.
    // The table is the boundary: backups accumulate one per rotation until retention caps them,
    // and the cap is the case an off-by-one gets wrong, so it is exercised on both sides.
    [InlineData(1, 0)]
    [InlineData(2, 1)]
    [InlineData(LogFile.BackupRetention, LogFile.BackupRetention - 1)]
    [InlineData(LogFile.BackupRetention + 1, LogFile.BackupRetention)]
    [InlineData(LogFile.BackupRetention + 5, LogFile.BackupRetention)]
    public void WriteRecord_AccumulatesBackupsUpToRetention(int writes, int expectedBackups)
    {
        var scope = new TempLog(rotationTriggerBytes: 64);
        try
        {
            scope.Log.Initialize();
            for (var index = 0; index < writes; index++)
            {
                scope.Log.WriteRecord(Record($"entry {index}"));
            }

            scope.Log.Dispose();

            Assert.Equal(expectedBackups, CountBackups(scope));
            Assert.False(File.Exists($"{scope.FilePath}.{LogFile.BackupRetention + 1}"));
        }
        finally
        {
            scope.Dispose();
        }
    }

    [Theory]
    // A record is well under a kilobyte and well over 64 bytes, so the two triggers sit on
    // opposite sides of the same two writes: rotation is driven by the threshold, not by the
    // write count.
    [InlineData(64L, 1)]
    [InlineData(1024L * 1024L, 0)]
    public void WriteRecord_RotatesOnlyOnceTheFilePassesTheTrigger(long triggerBytes, int expectedBackups)
    {
        var scope = new TempLog(triggerBytes);
        try
        {
            scope.Log.Initialize();
            scope.Log.WriteRecord(Record("first"));
            scope.Log.WriteRecord(Record("second"));
            scope.Log.Dispose();

            Assert.Equal(expectedBackups, CountBackups(scope));
        }
        finally
        {
            scope.Dispose();
        }
    }

    [Fact]
    public void WriteRecord_WhenRotationFails_ReportsOnceThenBacksOffByOneTrigger()
    {
        // A directory squatting on the ".1" name makes the archive rename fail on every
        // platform. The file is then still over the trigger, so without a back-off every
        // following record would retry and print the same failure again.
        const long trigger = 1024;
        var scope = new TempLog(rotationTriggerBytes: trigger);
        using var stderr = new StderrCapture();
        try
        {
            Directory.CreateDirectory($"{scope.FilePath}.1");
            scope.Log.Initialize();
            var big = new string('x', (int)trigger + 100);

            scope.Log.WriteRecord(Record(big));          // over the trigger, no attempt yet
            scope.Log.WriteRecord(Record("second"));     // attempt 1 fails; back off one trigger
            scope.Log.WriteRecord(Record("third"));      // inside the back-off window
            scope.Log.WriteRecord(Record("fourth"));     // still inside
            Assert.Equal(1, CountRotationFailures(stderr));

            scope.Log.WriteRecord(Record(big));          // pushes past the retry threshold
            scope.Log.WriteRecord(Record("sixth"));      // attempt 2
            scope.Log.Dispose();
            Assert.Equal(2, CountRotationFailures(stderr));

            var content = File.ReadAllText(scope.FilePath);
            foreach (var message in new[] { "second", "third", "fourth", "sixth" })
            {
                Assert.Contains(message, content, StringComparison.Ordinal);
            }
        }
        finally
        {
            scope.Dispose();
        }
    }

    [Fact]
    public void WriteRecord_WhenRotationAndReopenFail_ResumesAfterAccessIsRestored()
    {
        var clock = new FixedClock(DateTimeOffset.UtcNow);
        using var scope = new TempLog(rotationTriggerBytes: 64, timeProvider: clock);
        using var stderr = new StderrCapture();
        Assert.Null(scope.Log.Initialize());
        scope.Log.WriteRecord(Record("before outage"));

        // Delete sharing lets us rename the open file on Windows as well as Unix. A
        // directory at its former path then blocks both rotation and the append reopen.
        var displacedPath = $"{scope.FilePath}.displaced";
        File.Move(scope.FilePath, displacedPath);
        Directory.CreateDirectory(scope.FilePath);
        scope.Log.WriteRecord(Record("during outage"));
        Assert.Equal(1, CountRotationFailures(stderr));
        Assert.Equal(1, CountReopenFailures(stderr));

        for (var index = 0; index < 10; index++)
        {
            scope.Log.WriteRecord(Record("still unavailable"));
        }

        Assert.Equal(1, CountReopenFailures(stderr));
        clock.Advance(TimeSpan.FromMinutes(1));
        scope.Log.WriteRecord(Record("retry while unavailable"));
        Assert.Equal(2, CountReopenFailures(stderr));

        Directory.Delete(scope.FilePath);
        File.Move(displacedPath, scope.FilePath);
        clock.Advance(TimeSpan.FromMinutes(1));
        scope.Log.WriteRecord(Record("after recovery"));
        scope.Log.Dispose();

        var content = File.ReadAllText(scope.FilePath);
        Assert.Contains("before outage", content, StringComparison.Ordinal);
        Assert.Contains("after recovery", content, StringComparison.Ordinal);
        Assert.DoesNotContain("during outage", content, StringComparison.Ordinal);
        Assert.Equal(2, CountReopenFailures(stderr));
    }

    [Fact]
    public void WriteRecord_WhenDisposedAfterRotationAndReopenFail_DoesNotReopen()
    {
        var clock = new FixedClock(DateTimeOffset.UtcNow);
        using var scope = new TempLog(rotationTriggerBytes: 64, timeProvider: clock);
        using var stderr = new StderrCapture();
        Assert.Null(scope.Log.Initialize());
        scope.Log.WriteRecord(Record("before outage"));

        File.Move(scope.FilePath, $"{scope.FilePath}.displaced");
        Directory.CreateDirectory(scope.FilePath);
        scope.Log.WriteRecord(Record("during outage"));
        Assert.Equal(1, CountReopenFailures(stderr));
        scope.Log.Dispose();

        Directory.Delete(scope.FilePath);
        clock.Advance(TimeSpan.FromMinutes(1));
        scope.Log.WriteRecord(Record("after disposal"));

        Assert.False(File.Exists(scope.FilePath));
        Assert.Equal(1, CountReopenFailures(stderr));
    }

    [Fact]
    public void Initialize_WhenTheClockPassesTheRetentionWindow_DeletesTheBackup()
    {
        // The clock is injected rather than the file back-dated, so the retention window is
        // exercised by moving time instead of by mutating a real file's timestamp (G8).
        var clock = new FixedClock(DateTimeOffset.UtcNow);
        var scope = new TempLog(timeProvider: clock);
        try
        {
            var backup = $"{scope.FilePath}.1";
            File.WriteAllText(backup, "old backup");

            Assert.Null(scope.Log.Initialize());
            Assert.True(File.Exists(backup), "a backup inside the retention window must survive");
            scope.Log.Dispose();

            clock.Advance(TimeSpan.FromDays(LogFile.RetentionDays + 1));
            using var reopened = new LogFile(scope.FilePath, clock);
            Assert.Null(reopened.Initialize());

            Assert.False(File.Exists(backup));
        }
        finally
        {
            scope.Dispose();
        }
    }

    private static int CountRotationFailures(StderrCapture stderr)
    {
        var count = 0;
        foreach (var line in stderr.Text.Split('\n'))
        {
            if (line.Contains("failed to rotate log file", StringComparison.Ordinal))
            {
                count++;
            }
        }

        return count;
    }

    private static int CountReopenFailures(StderrCapture stderr) => stderr.Text
        .Split('\n')
        .Count(line => line.Contains("failed to reopen log file", StringComparison.Ordinal));

    // Counted by prefix rather than the glob "test.log.*", which on Windows also matches
    // "test.log" itself and would count the active file as a backup.
    private static int CountBackups(TempLog scope) => Array.FindAll(
        Directory.GetFiles(scope.DirectoryPath),
        path => Path.GetFileName(path).StartsWith("test.log.", StringComparison.Ordinal)).Length;

    private static LogRecord Record(string message) => new()
    {
        Timestamp = "2026-09-05T00:00:00.0000000Z",
        Level = "Information",
        Message = message,
        Module = "TestModule",
    };
}
