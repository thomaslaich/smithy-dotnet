using System.Net;
using System.Text;
using NSmithy.Aws;
using NSmithy.Client;
using NSmithy.Core;

namespace NSmithy.Tests.Aws;

public sealed class AwsCredentialsProviderTests
{
    [Fact]
    public async Task EnvironmentProviderReadsSessionCredentials()
    {
        var environment = new Dictionary<string, string?>
        {
            ["AWS_ACCESS_KEY_ID"] = "access",
            ["AWS_SECRET_ACCESS_KEY"] = "secret",
            ["AWS_SESSION_TOKEN"] = "token",
        };
        var provider = new EnvironmentAwsCredentialsProvider(name =>
            environment.GetValueOrDefault(name)
        );

        var credentials = await provider.GetCredentialsAsync();

        Assert.Equal("access", credentials.AccessKeyId);
        Assert.Equal("secret", credentials.SecretAccessKey);
        Assert.Equal("token", credentials.SessionToken);
    }

    [Fact]
    public async Task ProfileProviderMergesConfigAndCredentialsFiles()
    {
        using var directory = new TemporaryDirectory();
        var config = Path.Combine(directory.Path, "config");
        var credentials = Path.Combine(directory.Path, "credentials");
        File.WriteAllText(config, "[profile dev]\nregion = eu-west-1\n");
        File.WriteAllText(
            credentials,
            "[dev]\naws_access_key_id = profile-access\naws_secret_access_key = profile-secret\naws_session_token = profile-token\n"
        );
        var provider = new ProfileAwsCredentialsProvider(
            "dev",
            credentials,
            config,
            getEnvironmentVariable: static _ => null
        );

        var result = await provider.GetCredentialsAsync();

        Assert.Equal("profile-access", result.AccessKeyId);
        Assert.Equal("profile-secret", result.SecretAccessKey);
        Assert.Equal("profile-token", result.SessionToken);
    }

    [Fact]
    public async Task SsoProviderExchangesAnUnexpiredCliToken()
    {
        using var directory = new TemporaryDirectory();
        File.WriteAllText(
            Path.Combine(directory.Path, "token.json"),
            """
            {
              "startUrl": "https://example.awsapps.com/start",
              "accessToken": "cached-token",
              "expiresAt": "2026-08-24T18:00:00Z"
            }
            """
        );
        var handler = new RecordingHandler(request =>
        {
            Assert.Equal(
                "cached-token",
                request.Headers.GetValues("x-amz-sso_bearer_token").Single()
            );
            Assert.Contains(
                "account_id=123456789012",
                request.RequestUri!.Query,
                StringComparison.Ordinal
            );
            Assert.Contains(
                "role_name=Developer",
                request.RequestUri.Query,
                StringComparison.Ordinal
            );
            return JsonResponse(
                """
                {"roleCredentials":{"accessKeyId":"sso-access","secretAccessKey":"sso-secret","sessionToken":"sso-token","expiration":1787598000000}}
                """
            );
        });
        using var httpClient = new HttpClient(handler);
        var provider = new SsoAwsCredentialsProvider(
            "123456789012",
            "Developer",
            "eu-west-1",
            "https://example.awsapps.com/start",
            directory.Path,
            httpClient,
            new FixedTimeProvider(new DateTimeOffset(2026, 8, 24, 16, 0, 0, TimeSpan.Zero))
        );

        var credentials = await provider.GetCredentialsAsync();

        Assert.Equal("sso-access", credentials.AccessKeyId);
        Assert.Equal("sso-secret", credentials.SecretAccessKey);
        Assert.Equal("sso-token", credentials.SessionToken);
        Assert.NotNull(credentials.Expiration);
    }

    [Fact]
    public async Task InstanceMetadataProviderUsesImdsV2Token()
    {
        var calls = new List<string>();
        var handler = new RecordingHandler(request =>
        {
            calls.Add($"{request.Method} {request.RequestUri!.AbsolutePath}");
            if (request.Method == HttpMethod.Put)
            {
                Assert.Equal(
                    "21600",
                    request.Headers.GetValues("X-aws-ec2-metadata-token-ttl-seconds").Single()
                );
                return TextResponse("imds-token");
            }
            Assert.Equal(
                "imds-token",
                request.Headers.GetValues("X-aws-ec2-metadata-token").Single()
            );
            return request.RequestUri.AbsolutePath.EndsWith(
                "/security-credentials/",
                StringComparison.Ordinal
            )
                ? TextResponse("example-role\n")
                : JsonResponse(
                    """
                    {"Code":"Success","AccessKeyId":"imds-access","SecretAccessKey":"imds-secret","Token":"imds-token-value","Expiration":"2026-08-24T18:00:00Z"}
                    """
                );
        });
        using var httpClient = new HttpClient(handler);
        var provider = new InstanceMetadataAwsCredentialsProvider(
            httpClient,
            new Uri("http://127.0.0.1/"),
            getEnvironmentVariable: static _ => null
        );

        var credentials = await provider.GetCredentialsAsync();

        Assert.Equal("imds-access", credentials.AccessKeyId);
        Assert.Equal("imds-secret", credentials.SecretAccessKey);
        Assert.Equal("imds-token-value", credentials.SessionToken);
        Assert.Equal(
            [
                "PUT /latest/api/token",
                "GET /latest/meta-data/iam/security-credentials/",
                "GET /latest/meta-data/iam/security-credentials/example-role",
            ],
            calls
        );
    }

    [Fact]
    public async Task InstanceMetadataProviderHonorsDisabledV1Fallback()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        using var httpClient = new HttpClient(handler);
        var provider = new InstanceMetadataAwsCredentialsProvider(
            httpClient,
            new Uri("http://127.0.0.1/"),
            getEnvironmentVariable: name => name == "AWS_EC2_METADATA_V1_DISABLED" ? "true" : null
        );

        var exception = await Assert.ThrowsAsync<AwsCredentialsProviderException>(async () =>
            await provider.GetCredentialsAsync()
        );

        Assert.Contains("IMDSv2 token", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DefaultChainContinuesOnlyPastUnconfiguredProviders()
    {
        var expected = new AwsCredentials("access", "secret");
        var chain = new DefaultAwsCredentialsProvider([
            new StubProvider(
                new AwsCredentialsProviderException(
                    "first",
                    "not configured",
                    isNotConfigured: true
                )
            ),
            new StubProvider(expected),
        ]);

        var credentials = await chain.GetCredentialsAsync();

        Assert.Same(expected, credentials);
    }

    [Theory]
    [InlineData("us-east-1", "https://lambda.us-east-1.amazonaws.com/")]
    [InlineData("cn-north-1", "https://lambda.cn-north-1.amazonaws.com.cn/")]
    [InlineData("us-gov-west-1", "https://lambda.us-gov-west-1.amazonaws.com/")]
    [InlineData("us-iso-east-1", "https://lambda.us-iso-east-1.c2s.ic.gov/")]
    public async Task RegionalEndpointResolverUsesAwsPartitions(string region, string expected)
    {
        var resolver = new AwsRegionalEndpointResolver("lambda", region);

        var endpoint = await resolver.ResolveEndpointAsync(
            new SmithyEndpointParameters(
                new ShapeId("example", "Service"),
                new ShapeId("example", "Operation"),
                null,
                null
            )
        );

        Assert.Equal(expected, endpoint.Uri.AbsoluteUri);
        Assert.Same(resolver.StaticEndpoint, endpoint);
    }

    private sealed class StubProvider : IAwsCredentialsProvider
    {
        private readonly AwsCredentials? credentials;
        private readonly Exception? exception;

        public StubProvider(AwsCredentials credentials) => this.credentials = credentials;

        public StubProvider(Exception exception) => this.exception = exception;

        public ValueTask<AwsCredentials> GetCredentialsAsync(
            CancellationToken cancellationToken = default
        ) =>
            exception is null
                ? ValueTask.FromResult(credentials!)
                : ValueTask.FromException<AwsCredentials>(exception);
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) => Task.FromResult(respond(request));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory() =>
            Path = Directory.CreateTempSubdirectory("nsmithy-aws-").FullName;

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }

    private static HttpResponseMessage TextResponse(string value) =>
        new(HttpStatusCode.OK) { Content = new StringContent(value, Encoding.UTF8, "text/plain") };

    private static HttpResponseMessage JsonResponse(string value) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(value, Encoding.UTF8, "application/json"),
        };
}
