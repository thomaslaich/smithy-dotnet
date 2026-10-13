using NSmithy.Codecs.Json;
using Nsmithy.Tests.Naming;
using Nsmithy.Tests.Naming.Schemas;

namespace NSmithy.Tests.Core;

// Shapes in model/naming.smithy collide with names C# or the generator already uses. These tests
// pin the names the generator falls back to and check the wire names stay the modeled ones.
public sealed class GeneratedNamesTests
{
    [Fact]
    public void MemberNamedLikeItsStructureTakesTheValueSuffix()
    {
        var domain = new DomainName(CertificateArn: "arn", DomainNameValue: "example.com");
        var codec = JsonCodecFactory.Default.FromSchema(DomainNameSchema.Schema);

        var json = codec.SerializeText(domain);

        Assert.Equal("{\"domainName\":\"example.com\",\"certificateArn\":\"arn\"}", json);
        Assert.Equal(domain, codec.DeserializeText(json));
    }

    [Fact]
    public void MembersNamedLikeInheritedMembersTakeTheValueSuffix()
    {
        var selector = new Selector(EqualsValue: "a", ToStringValue: "b");
        var codec = JsonCodecFactory.Default.FromSchema(SelectorSchema.Schema);

        var json = codec.SerializeText(selector);

        Assert.Equal("{\"equals\":\"a\",\"toString\":\"b\"}", json);
        Assert.Equal(selector, codec.DeserializeText(json));
    }

    [Fact]
    public void EnumValuesNamedLikeExistingMembersTakeTheValueSuffix()
    {
        Assert.Equal("EQ", Operator.EqualsValue.Value);
        Assert.Equal("VALUE", Operator.ValueValue.Value);
        Assert.Equal("FROM_VALUE", Operator.FromValueValue.Value);
        Assert.Equal(1, (int)Level.EqualsValue);
        Assert.Equal(2, (int)Level.ToStringValue);
    }

    [Fact]
    public void ShapeNamedLikeAnInheritedMemberTakesTheValueSuffix()
    {
        var value = new EqualsValue(Field: "x");

        Assert.Equal("x", value.Field);
    }

    [Theory]
    [InlineData("value", "{\"value\":\"v\"}")]
    [InlineData("unknown", "{\"unknown\":\"u\"}")]
    [InlineData("pathElement", "{\"pathElement\":\"p\"}")]
    public void UnionVariantsNamedLikeUnionMembersTakeTheCaseSuffix(string variant, string json)
    {
        PathElement element = variant switch
        {
            "value" => PathElement.FromValue("v"),
            "unknown" => PathElement.FromUnknown("u"),
            _ => PathElement.FromPathElement("p"),
        };
        var codec = JsonCodecFactory.Default.FromSchema(PathElementSchema.Schema);

        Assert.Equal(json, codec.SerializeText(element));
        Assert.Equal(element, codec.DeserializeText(json));
        Assert.Equal(
            variant,
            element.Match(
                index: _ => "index",
                value: _ => "value",
                unknownValue: _ => "unknown",
                pathElement: _ => "pathElement",
                unknown: (tag, _) => tag
            )
        );
    }

    [Fact]
    public void UnionVariantClassesUseTheCaseSuffix()
    {
        Assert.IsType<PathElement.ValueCase>(PathElement.FromValue("v"));
        Assert.IsType<PathElement.UnknownCase>(PathElement.FromUnknown("u"));
        Assert.IsType<PathElement.PathElementCase>(PathElement.FromPathElement("p"));
        Assert.IsType<Filter.TagCase>(Filter.FromTag("t"));
        Assert.IsType<Filter.EqualsCase>(Filter.FromEquals("e"));
        Assert.IsType<Filter.Prefix>(Filter.FromPrefix("p"));
    }

    [Fact]
    public void SchemaClassesLiveInTheSchemasNamespace()
    {
        // The model has both Dataset and DatasetSchema; their schema classes cannot clash with the
        // DatasetSchema record because they live in the Schemas namespace.
        var dataset = new Nsmithy.Tests.Naming.DatasetSchema(Fields: "f");
        var codec = JsonCodecFactory.Default.FromSchema(DatasetSchemaSchema.Schema);

        Assert.Equal("Nsmithy.Tests.Naming.Schemas", typeof(DatasetSchemaSchema).Namespace);
        Assert.Equal(
            "nsmithy.tests.naming#Dataset",
            Nsmithy.Tests.Naming.Schemas.DatasetSchema.Schema.Id.ToString()
        );
        Assert.Equal(dataset, codec.DeserializeText(codec.SerializeText(dataset)));
    }

    [Fact]
    public void MemberNamedBuilderKeepsItsName()
    {
        var output = new PlatformOutput(Builder: "b");
        var codec = JsonCodecFactory.Default.FromSchema(PlatformOutputSchema.Schema);

        Assert.Equal("{\"builder\":\"b\"}", codec.SerializeText(output));
        Assert.Equal(output, codec.DeserializeText("{\"builder\":\"b\"}"));
    }
}
