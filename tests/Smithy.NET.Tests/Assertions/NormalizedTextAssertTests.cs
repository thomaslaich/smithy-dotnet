using Smithy.NET.Tests.Assertions;

namespace Smithy.NET.Tests;

public sealed class NormalizedTextAssertTests
{
    [Fact]
    public void EqualNormalizesLineEndingsAndTrailingWhitespace()
    {
        NormalizedTextAssert.Equal("structure Example {}\n", "structure Example {}\r\n\r\n");
    }
}
