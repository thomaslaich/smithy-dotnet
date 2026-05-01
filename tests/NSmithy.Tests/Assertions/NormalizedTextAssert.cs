namespace NSmithy.Tests.Assertions;

internal static class NormalizedTextAssert
{
    public static void Equal(string expected, string actual)
    {
        Assert.Equal(Normalize(expected), Normalize(actual));
    }

    private static string Normalize(string value)
    {
        return value.ReplaceLineEndings("\n").TrimEnd();
    }
}
