namespace Smithy.NET.Tests.GoldenFiles;

internal static class GoldenFile
{
    public static void AssertMatches(string expected, string actual)
    {
        Assert.Equal(Normalize(expected), Normalize(actual));
    }

    private static string Normalize(string value)
    {
        return value.ReplaceLineEndings("\n").TrimEnd();
    }
}
