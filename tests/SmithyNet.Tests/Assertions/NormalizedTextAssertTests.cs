using SmithyNet.Tests.Assertions;

namespace SmithyNet.Tests;

public sealed class NormalizedTextAssertTests
{
    [Fact]
    public void EqualNormalizesLineEndingsAndTrailingWhitespace()
    {
        NormalizedTextAssert.Equal("structure Example {}\n", "structure Example {}\r\n\r\n");
    }
}
