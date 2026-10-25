using System.Net;
using System.Text;
using AmazonS3.Com.Amazonaws.S3;
using NSmithy.Aws;
using NSmithy.Client;

namespace Endpoints.Conformance;

public sealed class S3ClientEndpointTests
{
    [Theory]
    [InlineData(
        false,
        null,
        "https://my-bucket.s3.us-west-2.amazonaws.com/dir/a%20b%2B%25%3F.jpg?versionId=v%2B1"
    )]
    [InlineData(
        true,
        null,
        "https://s3.us-west-2.amazonaws.com/my-bucket/dir/a%20b%2B%25%3F.jpg?versionId=v%2B1"
    )]
    [InlineData(
        true,
        "http://localhost:9000/base",
        "http://localhost:9000/base/my-bucket/dir/a%20b%2B%25%3F.jpg?versionId=v%2B1"
    )]
    public async Task ObjectPathsContainTheBucketExactlyWhereTheRulesPlaceIt(
        bool pathStyle,
        string? endpoint,
        string expected
    )
    {
        var handler = new RecordingHandler();
        using var client = Client(
            handler,
            new()
            {
                Region = "us-west-2",
                ForcePathStyle = pathStyle,
                Endpoint = endpoint is null ? null : new Uri(endpoint),
            }
        );

        await client.HeadObjectAsync(
            new HeadObjectInput("my-bucket", "dir/a b+%?.jpg", VersionId: "v+1")
        );

        Assert.Equal(expected, Assert.Single(handler.Requests).RequestUri!.AbsoluteUri);
        Assert.Contains("/us-west-2/s3/aws4_request", Authorization(handler.Requests[0]));
    }

    [Theory]
    [InlineData(false, "https://my-bucket.s3.us-west-2.amazonaws.com/")]
    [InlineData(true, "https://s3.us-west-2.amazonaws.com/my-bucket/")]
    public async Task BucketOnlyPathsHaveNoBucketSegment(bool pathStyle, string expected)
    {
        var handler = new RecordingHandler();
        using var client = Client(
            handler,
            new() { Region = "us-west-2", ForcePathStyle = pathStyle }
        );

        await client.HeadBucketAsync(new HeadBucketInput("my-bucket"));

        Assert.Equal(expected, Assert.Single(handler.Requests).RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task BucketQueryOperationsKeepTheirQuery()
    {
        var handler = new RecordingHandler(
            "<ListBucketResult xmlns=\"http://s3.amazonaws.com/doc/2006-03-01/\"/>"
        );
        using var client = Client(handler, new() { Region = "us-west-2" });

        await client.ListObjectsV2Async(new ListObjectsV2Input("my-bucket"));

        Assert.Equal(
            "https://my-bucket.s3.us-west-2.amazonaws.com/?list-type=2",
            Assert.Single(handler.Requests).RequestUri!.AbsoluteUri
        );
    }

    [Fact]
    public async Task OperationsWithoutBucketLabelsKeepTheirPath()
    {
        var handler = new RecordingHandler(
            "<ListAllMyBucketsResult xmlns=\"http://s3.amazonaws.com/doc/2006-03-01/\"/>"
        );
        using var client = Client(handler, new() { Region = "us-west-2" });

        await client.ListBucketsAsync(new ListBucketsInput());

        Assert.Equal(
            "https://s3.us-west-2.amazonaws.com/?x-id=ListBuckets",
            Assert.Single(handler.Requests).RequestUri!.AbsoluteUri
        );
    }

    [Fact]
    public async Task CustomResolversPlaceTheBucketThemselves()
    {
        var handler = new RecordingHandler();
        using var client = Client(
            handler,
            new() { EndpointResolver = new PathStyleResolver(new Uri("http://localhost:9000")) }
        );

        await client.HeadObjectAsync(new HeadObjectInput("my-bucket", "photo.jpg"));

        Assert.Equal(
            "http://localhost:9000/my-bucket/photo.jpg",
            Assert.Single(handler.Requests).RequestUri!.AbsoluteUri
        );
    }

    [Fact]
    public async Task RetriesKeepThePathAndSignature()
    {
        var handler = new RecordingHandler(failFirst: true);
        using var client = Client(
            handler,
            new()
            {
                Region = "us-west-2",
                RetryStrategy = new SmithySimpleRetryStrategy(maxAttempts: 2, delay: TimeSpan.Zero),
            }
        );

        await client.HeadObjectAsync(new HeadObjectInput("my-bucket", "dir/photo.jpg"));

        Assert.Equal(2, handler.Requests.Count);
        Assert.All(
            handler.Requests,
            request =>
                Assert.Equal(
                    "https://my-bucket.s3.us-west-2.amazonaws.com/dir/photo.jpg",
                    request.RequestUri!.AbsoluteUri
                )
        );
        Assert.Equal(Authorization(handler.Requests[0]), Authorization(handler.Requests[1]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResolverDecoratorsKeepAddressingAndSigningMetadata(bool pathStyle)
    {
        var handler = new RecordingHandler();
        var config = new AmazonS3ClientConfig { Region = "eu-west-1", ForcePathStyle = pathStyle };
        var resolver = new ForwardingResolver(new AmazonS3EndpointResolver(config));
        config.EndpointResolver = resolver;
        using var client = Client(handler, config);

        await client.HeadObjectAsync(new HeadObjectInput("my-bucket", "photo.jpg"));

        var request = Assert.Single(handler.Requests);
        Assert.Equal(
            pathStyle
                ? "https://s3.eu-west-1.amazonaws.com/my-bucket/photo.jpg"
                : "https://my-bucket.s3.eu-west-1.amazonaws.com/photo.jpg",
            request.RequestUri!.AbsoluteUri
        );
        Assert.Contains("/eu-west-1/s3/aws4_request", Authorization(request));
        Assert.Equal("forwarded", Assert.Single(request.Headers.GetValues("x-resolver")));
        Assert.NotNull(resolver.Operation);
        Assert.Equal("HeadObject", resolver.Operation!.Id.Name);
    }

    // A record copy keeps the generated endpoint's auth scheme properties.
    private sealed class ForwardingResolver(IEndpointResolver inner) : IEndpointResolver
    {
        public NSmithy.Core.Serde.IOperationSchema? Operation { get; private set; }

        public async ValueTask<SmithyEndpoint> ResolveEndpointAsync(
            SmithyEndpointParameters parameters,
            CancellationToken cancellationToken = default
        )
        {
            Operation = parameters.Operation;
            var endpoint = await inner.ResolveEndpointAsync(parameters, cancellationToken);
            return endpoint with
            {
                Uri = new Uri(endpoint.Uri.AbsoluteUri),
                Headers = new Dictionary<string, IReadOnlyList<string>>(
                    endpoint.Headers ?? new Dictionary<string, IReadOnlyList<string>>()
                )
                {
                    ["x-resolver"] = ["forwarded"],
                },
            };
        }
    }

    // The modeled S3 paths have no bucket segment, so a custom resolver adds the bucket to the
    // endpoint, here path-style.
    private sealed class PathStyleResolver(Uri baseUri) : IEndpointResolver
    {
        public ValueTask<SmithyEndpoint> ResolveEndpointAsync(
            SmithyEndpointParameters parameters,
            CancellationToken cancellationToken = default
        ) =>
            ValueTask.FromResult(
                new SmithyEndpoint(
                    parameters.Input is HeadObjectInput input
                        ? new Uri(baseUri, input.Bucket)
                        : baseUri
                )
            );
    }

    private static AmazonS3Client Client(RecordingHandler handler, AmazonS3ClientConfig config)
    {
        config.AuthSchemes.Add(
            new AwsSigV4AuthScheme(
                "s3",
                "us-west-2",
                new StaticAwsCredentialsProvider(new AwsCredentials("AKID", "SECRET")),
                new FixedTime()
            )
        );
        return new AmazonS3Client(new HttpClient(handler), config);
    }

    private static string Authorization(HttpRequestMessage request) =>
        string.Join(", ", request.Headers.GetValues("Authorization"));

    private sealed class FixedTime : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
    }

    private sealed class RecordingHandler(string body = "", bool failFirst = false)
        : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Requests.Add(request);
            if (failFirst && Requests.Count == 1)
            {
                throw new HttpRequestException("Transient failure");
            }
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/xml"),
                }
            );
        }
    }
}
