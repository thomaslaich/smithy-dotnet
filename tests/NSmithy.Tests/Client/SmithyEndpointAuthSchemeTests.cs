using NSmithy.Client;
using NSmithy.Core;

namespace NSmithy.Tests.Client;

public sealed class SmithyEndpointAuthSchemeTests
{
    private static readonly SmithyEndpointAuthScheme SigV4Default = new(
        "aws.auth#sigv4",
        new Dictionary<string, Document>
        {
            ["signingName"] = Document.From("iam"),
            ["signingRegion"] = Document.From("eu-central-1"),
        }
    );

    [Fact]
    public void ASchemeTakesTheDefaultsForThePropertiesItLeavesOut()
    {
        // IAM's rules set the signing region of the global endpoint but no signing name.
        var scheme = new SmithyEndpointAuthScheme(
            "aws.auth#sigv4",
            new Dictionary<string, Document> { ["signingRegion"] = Document.From("us-east-1") }
        );

        var merged = Assert.Single(SmithyEndpointAuthScheme.WithDefaults([scheme], [SigV4Default]));

        Assert.Equal("iam", merged.GetString("signingName"));
        Assert.Equal("us-east-1", merged.GetString("signingRegion"));
    }

    [Fact]
    public void ASchemeWithoutPropertiesTakesAllDefaults()
    {
        var merged = Assert.Single(
            SmithyEndpointAuthScheme.WithDefaults(
                [new SmithyEndpointAuthScheme("aws.auth#sigv4")],
                [SigV4Default]
            )
        );

        Assert.Equal("iam", merged.GetString("signingName"));
        Assert.Equal("eu-central-1", merged.GetString("signingRegion"));
    }

    [Fact]
    public void ASchemeWithoutADefaultOfItsIdIsUnchanged()
    {
        var scheme = new SmithyEndpointAuthScheme(
            "aws.auth#sigv4a",
            new Dictionary<string, Document> { ["signingName"] = Document.From("s3") }
        );

        var merged = Assert.Single(SmithyEndpointAuthScheme.WithDefaults([scheme], [SigV4Default]));

        Assert.Same(scheme, merged);
    }
}
