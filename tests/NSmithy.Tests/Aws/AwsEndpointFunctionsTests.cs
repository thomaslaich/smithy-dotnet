using NSmithy.Aws;

namespace NSmithy.Tests.Aws;

// Vectors from the smithy-aws-endpoints tests of the reference implementation (AwsArnTest,
// AwsPartitionTest, IsVirtualHostableS3BucketTest).
public sealed class AwsEndpointFunctionsTests
{
    [Theory]
    [InlineData("us-west-2", "aws", "amazonaws.com", "api.aws", "us-east-1")]
    [InlineData("aws-global", "aws", "amazonaws.com", "api.aws", "us-east-1")]
    [InlineData(
        "cn-north-1",
        "aws-cn",
        "amazonaws.com.cn",
        "api.amazonwebservices.com.cn",
        "cn-northwest-1"
    )]
    [InlineData("us-gov-west-1", "aws-us-gov", "amazonaws.com", "api.aws", "us-gov-west-1")]
    [InlineData(
        "eusc-de-east-1",
        "aws-eusc",
        "amazonaws.eu",
        "api.amazonwebservices.eu",
        "eusc-de-east-1"
    )]
    public void PartitionOfAListedRegion(
        string region,
        string name,
        string dnsSuffix,
        string dualStackDnsSuffix,
        string implicitGlobalRegion
    )
    {
        var partition = AwsEndpointFunctions.Partition(region);

        Assert.Equal(
            new AwsPartition(name, dnsSuffix, dualStackDnsSuffix, true, true, implicitGlobalRegion),
            partition
        );
    }

    [Theory]
    [InlineData("us-west-3", "aws")]
    [InlineData("cn-south-7", "aws-cn")]
    [InlineData("us-iso-west-9", "aws-iso")]
    [InlineData("us-isob-north-1", "aws-iso-b")]
    public void PartitionOfAnUnlistedRegionComesFromTheRegionPattern(string region, string name)
    {
        Assert.Equal(name, AwsEndpointFunctions.Partition(region).Name);
    }

    [Theory]
    [InlineData("mars-east-1")]
    [InlineData("")]
    // \d in the partition patterns matches ASCII digits only.
    [InlineData("us-west-٣")]
    public void PartitionOfAnUnknownRegionIsAws(string region)
    {
        Assert.Equal("aws", AwsEndpointFunctions.Partition(region).Name);
    }

    [Fact]
    public void ParseArnSplitsTheResourceOnColonsAndSlashes()
    {
        var arn = AwsEndpointFunctions.ParseArn(
            "arn:aws:s3:us-east-2:012345678:outpost/op-1234:bucket:mybucket"
        );

        Assert.NotNull(arn);
        Assert.Equal("aws", arn.Partition);
        Assert.Equal("s3", arn.Service);
        Assert.Equal("us-east-2", arn.Region);
        Assert.Equal("012345678", arn.AccountId);
        Assert.Equal(["outpost", "op-1234", "bucket", "mybucket"], arn.ResourceId);
    }

    [Fact]
    public void ParseArnAllowsAnEmptyRegionAndAccount()
    {
        var arn = AwsEndpointFunctions.ParseArn("arn:aws:iam:::role/admin");

        Assert.NotNull(arn);
        Assert.Equal("", arn.Region);
        Assert.Equal("", arn.AccountId);
        Assert.Equal(["role", "admin"], arn.ResourceId);
    }

    [Fact]
    public void ParseArnKeepsAnEmptyTrailingResourcePart()
    {
        var arn = AwsEndpointFunctions.ParseArn("arn:aws:s3:us-east-2:012345678:outpost:");

        Assert.NotNull(arn);
        Assert.Equal(["outpost", ""], arn.ResourceId);
    }

    [Theory]
    [InlineData("arn:aws:s3:us-east-2:012345678:")]
    [InlineData("arn:aws:s3:us-east-2")]
    [InlineData("arn::s3:us-east-2:012345678:bucket")]
    [InlineData("arn:aws::us-east-2:012345678:bucket")]
    [InlineData("urn:aws:s3:us-east-2:012345678:bucket")]
    [InlineData("arn:")]
    public void ParseArnRejectsMalformedArns(string value)
    {
        Assert.Null(AwsEndpointFunctions.ParseArn(value));
    }

    [Theory]
    [InlineData("ab", false, false)]
    [InlineData("abc", false, true)]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", false, true)]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", false, false)]
    [InlineData("-abc", false, false)]
    [InlineData(".abc", true, false)]
    [InlineData("abc-", false, false)]
    [InlineData("abc.", true, false)]
    [InlineData("a-b-c", false, true)]
    [InlineData("a.b-c", false, false)]
    [InlineData("abc", true, true)]
    [InlineData("a-b-c", true, true)]
    [InlineData("a.b-c", true, true)]
    [InlineData("a..b", true, false)]
    [InlineData("a-.b", true, false)]
    [InlineData("a.-b", true, false)]
    [InlineData("192.168.1.1", true, false)]
    [InlineData("999.999.999.999", true, false)]
    [InlineData("192.168.1.word", true, true)]
    [InlineData("a-b.c-d", true, true)]
    [InlineData("a0b1c2", true, true)]
    [InlineData("123", true, true)]
    [InlineData("abc123", true, true)]
    [InlineData("123abc", true, true)]
    [InlineData("a0.b1.c2", true, true)]
    [InlineData("abc_def", true, false)]
    [InlineData("abcDef", true, false)]
    [InlineData("abc#def", true, false)]
    public void IsVirtualHostableS3Bucket(string value, bool allowSubDomains, bool expected)
    {
        Assert.Equal(
            expected,
            AwsEndpointFunctions.IsVirtualHostableS3Bucket(value, allowSubDomains)
        );
    }
}
