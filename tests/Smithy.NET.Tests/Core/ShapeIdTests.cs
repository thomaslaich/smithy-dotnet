using Smithy.NET.Core;

namespace Smithy.NET.Tests.Core;

public sealed class ShapeIdTests
{
    [Fact]
    public void ParseReadsAbsoluteShapeId()
    {
        var id = ShapeId.Parse("example.weather#Forecast$city");

        Assert.Equal("example.weather", id.Namespace);
        Assert.Equal("Forecast", id.Name);
        Assert.Equal("city", id.MemberName);
        Assert.Equal("example.weather#Forecast$city", id.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("Forecast")]
    [InlineData("example.weather#")]
    [InlineData("example-weather#Forecast")]
    [InlineData("example.weather#Forecast$")]
    public void TryParseRejectsInvalidShapeIds(string value)
    {
        Assert.False(ShapeId.TryParse(value, out _));
    }
}
