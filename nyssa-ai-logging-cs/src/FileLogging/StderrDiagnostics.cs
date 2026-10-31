namespace NyssaAI.Logging.FileLogging;

/// <summary>
/// StderrDiagnostics is the one place the library writes its own diagnostics (a destination
/// that could not be wired, a rotation that failed) to standard error. It owns the line
/// format, so every diagnostic looks like a stderr log line and uses the platform line
/// ending, and it never throws: a broken stderr is the one failure logging cannot report.
/// </summary>
internal static class StderrDiagnostics
{
    /// <summary>Write an error-level diagnostic about <paramref name="subject"/>.</summary>
    /// <param name="subject">The consumer name or path the diagnostic concerns.</param>
    /// <param name="detail">What went wrong.</param>
    public static void Error(string subject, string detail) => Write("erro", subject, detail);

    /// <summary>Write a warning-level diagnostic about <paramref name="subject"/>.</summary>
    /// <param name="subject">The consumer name or path the diagnostic concerns.</param>
    /// <param name="detail">What went wrong.</param>
    public static void Warning(string subject, string detail) => Write("warn", subject, detail);

    /// <summary>
    /// Format one line the way the stderr handler does: a four-character level tag, the
    /// subject, then the detail indented on a continuation line.
    /// </summary>
    /// <param name="levelTag">The four-character level tag.</param>
    /// <param name="subject">The consumer name or path the line concerns.</param>
    /// <param name="detail">The message body.</param>
    public static string Format(string levelTag, string subject, string detail) =>
        $"{levelTag}: {subject}{Environment.NewLine}      {detail}";

    /// <summary>Write a preformatted line to stderr, swallowing any failure to do so.</summary>
    /// <param name="line">The line to write.</param>
    public static void WriteLine(string line)
    {
        try
        {
            Console.Error.WriteLine(line);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Nowhere left to report to.
        }
    }

    private static void Write(string levelTag, string subject, string detail) =>
        WriteLine(Format(levelTag, subject, detail));
}
