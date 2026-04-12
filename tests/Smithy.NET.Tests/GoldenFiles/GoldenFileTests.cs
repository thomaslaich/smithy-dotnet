using Smithy.NET.Tests.GoldenFiles;

namespace Smithy.NET.Tests;

public sealed class GoldenFileTests
{
    [Fact]
    public void AssertMatchesNormalizesLineEndingsAndTrailingWhitespace()
    {
        GoldenFile.AssertMatches("structure Example {}\n", "structure Example {}\r\n\r\n");
    }
}
