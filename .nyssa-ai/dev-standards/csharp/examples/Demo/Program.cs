using Standards.Sample.Email;
using Standards.Sample.Formatting;

namespace Standards.Sample.Demo;

internal static class Program
{
    private static int Main()
    {
        var parsed = EmailAddress.Create(" Ada@EXAMPLE.COM ");
        if (parsed.IsFailure) { Console.Error.WriteLine("Sample prerequisite failed."); return 1; }
        Console.WriteLine("normalized: " + parsed.Value.Value);
        foreach (IEmailFormatter formatter in new IEmailFormatter[] { new EmailFormatterText(), new EmailFormatterJson() })
            Console.WriteLine(formatter.Format(parsed.Value));
        var invalid = EmailAddress.Create("not-an-address");
        if (invalid.IsSuccess) { Console.Error.WriteLine("Expected failure was missing."); return 1; }
        Console.WriteLine("rejected: Validation");
        return 0;
    }
}
