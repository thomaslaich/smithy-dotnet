using System.Net;
using NSmithy.Aws;
using NSmithy.Client;
using NSmithy.Http;

namespace NSmithy.Tests.Aws;

public sealed class AwsSigV4MiddlewareTests
{
    [Fact]
    public async Task SignAddsSigV4Headers()
    {
        var request = new SmithyHttpRequest(HttpMethod.Post, "/");
        request.Content = "{}"u8.ToArray();
        request.ContentType = "application/x-amz-json-1.0";
        request.Headers["X-Amz-Target"] = ["DynamoDB_20120810.ListTables"];

        var middleware = new AwsSigV4Middleware(
            new Uri("http://localhost:4566"),
            "dynamodb",
            "us-east-1",
            new StaticAwsCredentialsProvider(
                new AwsCredentials("AKIDEXAMPLE", "wJalrXUtnFEMI/K7MDENG+bPxRfiCYEXAMPLEKEY")
            ),
            new FixedTimeProvider(new DateTimeOffset(2026, 06, 22, 12, 34, 56, TimeSpan.Zero))
        );

        _ = await middleware.InvokeAsync(
            new SmithyOperationRequest("DynamoDB", "ListTables", request),
            (operationRequest, _) =>
                Task.FromResult(
                    new SmithyOperationResponse(
                        operationRequest.ServiceName,
                        operationRequest.OperationName,
                        new SmithyHttpResponse(
                            HttpStatusCode.OK,
                            "OK",
                            [],
                            new Dictionary<string, IReadOnlyList<string>>(),
                            new Dictionary<string, IReadOnlyList<string>>()
                        )
                    )
                )
        );

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
    public async Task InvokeSignsThenCallsNextMiddleware()
    {
        var middleware = new AwsSigV4Middleware(
            new Uri("http://localhost:4566"),
            "s3",
            "us-east-1",
            new StaticAwsCredentialsProvider(new AwsCredentials("test", "test", "token")),
            TimeProvider.System
        );
        var httpRequest = new SmithyHttpRequest(HttpMethod.Get, "/");
        var operationRequest = new SmithyOperationRequest("S3", "ListBuckets", httpRequest);

        var response = await middleware.InvokeAsync(
            operationRequest,
            (request, _) =>
            {
                Assert.Same(operationRequest, request);
                Assert.True(httpRequest.Headers.ContainsKey("Authorization"));
                Assert.Equal(["token"], httpRequest.Headers["X-Amz-Security-Token"]);
                return Task.FromResult(
                    new SmithyOperationResponse(
                        request.ServiceName,
                        request.OperationName,
                        new SmithyHttpResponse(
                            HttpStatusCode.OK,
                            "OK",
                            [],
                            new Dictionary<string, IReadOnlyList<string>>(),
                            new Dictionary<string, IReadOnlyList<string>>()
                        )
                    )
                );
            }
        );

        Assert.Equal(HttpStatusCode.OK, response.Response.StatusCode);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
