using System.Net;
using System.Text;
using AWSSecurityTokenServiceV20110615.Com.Amazonaws.Sts;
using NSmithy.Aws;

namespace Endpoints.Conformance;

/// <summary>
/// Sends requests through a generated AWS client: the endpoint comes from the modeled rules and
/// the configured region, and SigV4 signs with the scope the endpoint names, if any.
/// </summary>
public sealed class AwsClientEndpointTests
{
    [Fact]
    public async Task RegionalRequestsGoToTheConfiguredRegion()
    {
        var handler = new RecordingHandler();
        using var client = Client(handler, new() { Region = "eu-central-1" });

        await client.GetCallerIdentityAsync(new GetCallerIdentityInput());

        Assert.Equal(
            new Uri("https://sts.eu-central-1.amazonaws.com/"),
            handler.Request!.RequestUri
        );
        Assert.Contains("/eu-central-1/sts/aws4_request", Authorization(handler));
    }

    [Fact]
    public async Task GlobalEndpointSignsForUsEast1FromALegacyRegion()
    {
        var handler = new RecordingHandler();
        using var client = Client(
            handler,
            new() { Region = "eu-west-1", UseGlobalEndpoint = true }
        );

        await client.GetCallerIdentityAsync(new GetCallerIdentityInput());

        Assert.Equal(new Uri("https://sts.amazonaws.com/"), handler.Request!.RequestUri);
        Assert.Contains("/us-east-1/sts/aws4_request", Authorization(handler));
    }

    [Fact]
    public async Task FipsAndDualStackSelectTheirHostNames()
    {
        var handler = new RecordingHandler();
        using var client = Client(
            handler,
            new()
            {
                Region = "us-west-2",
                UseFips = true,
                UseDualStack = true,
            }
        );

        await client.GetCallerIdentityAsync(new GetCallerIdentityInput());

        Assert.Equal(new Uri("https://sts-fips.us-west-2.api.aws/"), handler.Request!.RequestUri);
    }

    private static AWSSecurityTokenServiceV20110615Client Client(
        RecordingHandler handler,
        AWSSecurityTokenServiceV20110615ClientConfig config
    )
    {
        // Endpoints without auth scheme properties sign with the scheme's own region.
        config.AuthSchemes.Add(
            new AwsSigV4AuthScheme(
                "sts",
                config.Region!,
                new StaticAwsCredentialsProvider(new AwsCredentials("AKID", "SECRET"))
            )
        );
        return new AWSSecurityTokenServiceV20110615Client(new HttpClient(handler), config);
    }

    private static string Authorization(RecordingHandler handler) =>
        string.Join(", ", handler.Request!.Headers.GetValues("Authorization"));

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Request = request;
            const string body = """
                <GetCallerIdentityResponse xmlns="https://sts.amazonaws.com/doc/2011-06-15/">
                  <GetCallerIdentityResult>
                    <Arn>arn:aws:iam::123456789012:user/test</Arn>
                    <UserId>AIDA</UserId>
                    <Account>123456789012</Account>
                  </GetCallerIdentityResult>
                </GetCallerIdentityResponse>
                """;
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, Encoding.UTF8, "text/xml"),
                }
            );
        }
    }
}
