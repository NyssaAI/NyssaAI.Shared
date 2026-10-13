using Xunit;

namespace NyssaAI.Configuration.UseCaseTests.fixtures;

/// <summary>Basic C# values supplied to the same typed configuration workflow.</summary>
public static class ConfigurationSettingExamples
{
    /// <summary>Strings that must retain their text through either serializer overload.</summary>
    public static TheoryData<string, bool> StringValues
    {
        get
        {
            var cases = new TheoryData<string, bool>();
            foreach (var value in new[] { "smtp.example.test", "", "   ", "123", "true", "null", "\"quoted\"", "{\"port\":587}", "{invalid", "C:\\mail\n\tGrüße" })
            {
                cases.Add(value, false);
                cases.Add(value, true);
            }
            return cases;
        }
    }

    public static TheoryData<object, object> BasicValues => new()
    {
        { true, true },
        { false, false },
        { (byte)255, (byte)255 },
        { (sbyte)-8, (sbyte)-8 },
        { (short)-32000, (short)-32000 },
        { (ushort)65000, (ushort)65000 },
        { -42, -42 },
        { 42U, 42U },
        { -9000000000L, -9000000000L },
        { 18000000000000000000UL, 18000000000000000000UL },
        { 1.25F, 1.25F },
        { 2.5D, 2.5D },
        { 19.95M, 19.95M },
        { 'M', 'M' },
        { "weekly report", "weekly report" },
    };
}
