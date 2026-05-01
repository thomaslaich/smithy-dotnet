using NSmithy.Core;

namespace NSmithy.Tests.Core;

public sealed class DocumentTests
{
    [Fact]
    public void FromJsonElementPreservesNestedDocumentValues()
    {
        var document = System.Text.Json.JsonDocument.Parse(
            """
            {
              "name": "example",
              "enabled": true,
              "count": 3,
              "tags": ["a", "b"]
            }
            """
        );

        var value = Document.FromJsonElement(document.RootElement);

        Assert.Equal(DocumentKind.Object, value.Kind);
        Assert.Equal("example", value.AsObject()["name"].AsString());
        Assert.True(value.AsObject()["enabled"].AsBoolean());
        Assert.Equal(3m, value.AsObject()["count"].AsNumber());
        Assert.Equal("b", value.AsObject()["tags"].AsArray()[1].AsString());
    }
}
