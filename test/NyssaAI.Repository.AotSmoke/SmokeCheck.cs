
namespace NyssaAI.Repository.AotSmoke;

internal static class SmokeCheck
{
    internal static bool Completes(Action action)
    {
        action();
        return true;
    }

    internal static async Task<bool> CompletesAsync(Func<Task> action)
    {
        await action();
        return true;
    }

    internal static bool Throws(Action action)
    {
        Capture(action);
        return true;
    }

    internal static async Task<bool> ThrowsAsync(Func<Task> action)
    {
        await CaptureAsync(action);
        return true;
    }

    internal static Exception Capture(Action action)
    {
        try { action(); }
        catch (Exception exception) { return exception; }
        throw new InvalidOperationException("Expected a repository exception.");
    }

    internal static async Task<Exception> CaptureAsync(Func<Task> action)
    {
        try { await action(); }
        catch (Exception exception) { return exception; }
        throw new InvalidOperationException("Expected a repository or cancellation exception.");
    }

}
