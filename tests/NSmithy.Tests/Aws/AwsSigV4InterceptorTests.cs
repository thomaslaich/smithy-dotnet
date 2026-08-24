using System.Net;
using NSmithy.Aws;
using NSmithy.Client;
using NSmithy.Http;

namespace NSmithy.Tests.Aws;

public sealed class AwsSigV4InterceptorTests
{
    [Fact]
    public async Task SignAddsSigV4Headers()
    {
        var request = new SmithyHttpRequest(HttpMethod.Post, "/");
        request.Body = new SmithyHttpBody.Bytes("{}"u8.ToArray());
        request.ContentType = "application/x-amz-json-1.0";
        request.Headers["X-Amz-Target"] = ["DynamoDB_20120810.ListTables"];

        var interceptor = new AwsSigV4Interceptor(
            new Uri("http://localhost:4566"),
            "dynamodb",
            "us-east-1",
            new StaticAwsCredentialsProvider(
                new AwsCredentials("AKIDEXAMPLE", "wJalrXUtnFEMI/K7MDENG+bPxRfiCYEXAMPLEKEY")
            ),
            new FixedTimeProvider(new DateTimeOffset(2026, 06, 22, 12, 34, 56, TimeSpan.Zero))
        );

        var context = new SmithyContext();
        context.Set(SmithyContextKeys.ServiceName, "DynamoDB");
        context.Set(SmithyContextKeys.OperationName, "ListTables");
        _ = await interceptor.OnBeforeTransmitAsync(context, request);

        Assert.Equal(["localhost:4566"], request.Headers["Host"]);
        Assert.Equal(["20260622T123456Z"], request.Headers["X-Amz-Date"]);
        Assert.Equal(
            ["44136fa355b3678a1146ad16f7e8649e94fb4fc21fe77e8310c060f61caaff8a"],
            request.Headers["X-Amz-Content-Sha256"]
        );

        var authorization = Assert.Single(request.Headers["Authorization"]);
        Assert.StartsWith(
            "AWS4-HMAC-SHA256 Credential=AKIDEXAMPLE/20260622/us-east-1/dynamodb/aws4_request",
            authorization,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "SignedHeaders=content-type;host;x-amz-content-sha256;x-amz-date;x-amz-target",
            authorization,
            StringComparison.Ordinal
        );
        Assert.Contains("Signature=", authorization, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InterceptorSignsBeforeTransmit()
    {
        var interceptor = new AwsSigV4Interceptor(
            new Uri("http://localhost:4566"),
            "s3",
            "us-east-1",
            new StaticAwsCredentialsProvider(new AwsCredentials("test", "test", "token")),
            TimeProvider.System
        );
        var context = new SmithyContext();
        context.Set(SmithyContextKeys.ServiceName, "S3");
        context.Set(SmithyContextKeys.OperationName, "ListBuckets");
        var request = new SmithyHttpRequest(HttpMethod.Get, "/");

        var signed = await interceptor.OnBeforeTransmitAsync(context, request);

        Assert.Same(request, signed);
        Assert.True(request.Headers.ContainsKey("Authorization"));
        Assert.Equal(["token"], request.Headers["X-Amz-Security-Token"]);
    }

    [Fact]
    public async Task SignMatchesAwsPublishedS3GetObjectVector()
    {
        var request = new SmithyHttpRequest(
            HttpMethod.Get,
            "https://examplebucket.s3.amazonaws.com/test.txt"
        );
        request.Headers["Range"] = ["bytes=0-9"];
        var signer = new AwsSigV4Signer(
            "s3",
            "us-east-1",
            new FixedTimeProvider(new DateTimeOffset(2013, 05, 24, 0, 0, 0, TimeSpan.Zero))
        );

        await signer.SignAsync(
            new SmithyContext(),
            request,
            new AwsCredentials(
                "AKIAIOSFODNN7EXAMPLE",
                "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY"
            )
        );

        Assert.Equal(
            "AWS4-HMAC-SHA256 Credential=AKIAIOSFODNN7EXAMPLE/20130524/us-east-1/s3/aws4_request, SignedHeaders=host;range;x-amz-content-sha256;x-amz-date, Signature=f0e8bdb87c964420e857bd35b5d6ed310bd44f0170aba48dd91039c6036bdb41",
            Assert.Single(request.Headers["Authorization"])
        );
    }

    [Fact]
    public async Task SigningTheSameRequestTwiceIsDeterministic()
    {
        var request = new SmithyHttpRequest(HttpMethod.Get, "https://service.us-east-1.amazonaws.com/");
        var signer = new AwsSigV4Signer(
            "service",
            "us-east-1",
            new FixedTimeProvider(new DateTimeOffset(2026, 06, 22, 12, 34, 56, TimeSpan.Zero))
        );
        var credentials = new AwsCredentials("access", "secret", "token");

        await signer.SignAsync(new SmithyContext(), request, credentials);
        var first = Assert.Single(request.Headers["Authorization"]);
        await signer.SignAsync(new SmithyContext(), request, credentials);

        Assert.Equal(first, Assert.Single(request.Headers["Authorization"]));
    }

    [Fact]
    public void PresignMatchesAwsPublishedS3Vector()
    {
        var request = new SmithyHttpRequest(
            HttpMethod.Get,
            "https://examplebucket.s3.amazonaws.com/test.txt"
        );
        var signer = new AwsSigV4Signer(
            "s3",
            "us-east-1",
            new FixedTimeProvider(new DateTimeOffset(2013, 05, 24, 0, 0, 0, TimeSpan.Zero))
        );

        var uri = signer.Presign(
            request,
            new AwsCredentials(
                "AKIAIOSFODNN7EXAMPLE",
                "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY"
            ),
            TimeSpan.FromDays(1)
        );

        Assert.Equal(
            "https://examplebucket.s3.amazonaws.com/test.txt?X-Amz-Algorithm=AWS4-HMAC-SHA256&X-Amz-Credential=AKIAIOSFODNN7EXAMPLE%2F20130524%2Fus-east-1%2Fs3%2Faws4_request&X-Amz-Date=20130524T000000Z&X-Amz-Expires=86400&X-Amz-SignedHeaders=host&X-Amz-Signature=aeeed9bbccd4d02ee5c0109b86d86835f995330da4c265957d157751f604d404",
            uri.AbsoluteUri
        );
        Assert.Equal(uri.AbsoluteUri, request.RequestUri);
    }

    [Fact]
    public void PresignPreservesNonAuthenticationXAmzQueryParameters()
    {
        var request = new SmithyHttpRequest(
            HttpMethod.Get,
            "https://service.us-east-1.amazonaws.com/?X-Amz-Custom=value&X-Amz-Signature=stale"
        );
        var signer = new AwsSigV4Signer(
            "service",
            "us-east-1",
            new FixedTimeProvider(new DateTimeOffset(2026, 06, 22, 12, 34, 56, TimeSpan.Zero))
        );

        var uri = signer.Presign(
            request,
            new AwsCredentials("access", "secret"),
            TimeSpan.FromMinutes(5)
        );

        Assert.Contains("X-Amz-Custom=value", uri.Query, StringComparison.Ordinal);
        Assert.DoesNotContain("X-Amz-Signature=stale", uri.Query, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(604801)]
    public void PresignRejectsDurationsOutsideAwsLimits(int seconds)
    {
        var request = new SmithyHttpRequest(HttpMethod.Get, "https://s3.amazonaws.com/key");
        var signer = new AwsSigV4Signer("s3", "us-east-1");

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            signer.Presign(request, new AwsCredentials("access", "secret"), TimeSpan.FromSeconds(seconds))
        );
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
