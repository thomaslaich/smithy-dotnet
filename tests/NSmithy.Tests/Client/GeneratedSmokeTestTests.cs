using System.Net;
using System.Text;
using NSmithy.Client;
using Nsmithy.Tests.Smoke;

namespace NSmithy.Tests.Client;

/// <summary>
/// Runs the smoke tests generated from <c>model/smoke.smithy</c> against a fake service.
/// </summary>
public sealed class GeneratedSmokeTestTests
{
    [Fact]
    public void EachCaseBecomesATestWithItsOperationAndTags()
    {
        Assert.Equal(
            ["GetJobSucceeds", "GetJobIsMissing", "GetJobIsExpired", "GetJobFails"],
            JobsSmokeTests.All.Select(test => test.Id)
        );
        var succeeds = JobsSmokeTests.All[0];
        Assert.Equal("GetJob", succeeds.Operation);
        Assert.Equal(["read"], succeeds.Tags);
        Assert.True(succeeds.Expectation.IsSuccess);
        Assert.Equal("nsmithy.tests.smoke#JobNotFound", JobsSmokeTests.All[1].Expectation.ErrorId);
    }

    [Fact]
    public async Task EveryTestPassesAgainstAServiceThatAnswersAsModeled()
    {
        var results = await RunAllAsync(Service);

        Assert.All(results, result => Assert.True(result.Passed, result.Failure));
        Assert.IsType<JobNotFound>(results[1].Exception);
        Assert.Equal(
            "JobExpired",
            Assert.IsType<SmithyClientException>(results[2].Exception).ErrorCode
        );
    }

    [Fact]
    public async Task FailureTestsFailWhenTheCallSucceeds()
    {
        var results = await RunAllAsync(_ => Json(HttpStatusCode.OK, """{"status":"done"}"""));

        Assert.True(results[0].Passed);
        Assert.All(
            results.Skip(1),
            result =>
            {
                Assert.False(result.Passed);
                Assert.Contains("but the call succeeded", result.Failure, StringComparison.Ordinal);
            }
        );
    }

    [Fact]
    public async Task AnErrorOtherThanTheExpectedOneFailsTheTest()
    {
        var results = await RunAllAsync(_ => Error(HttpStatusCode.Conflict, "Conflict"));

        Assert.False(results[1].Passed);
        Assert.Contains(
            "nsmithy.tests.smoke#JobNotFound",
            results[1].Failure,
            StringComparison.Ordinal
        );
        Assert.True(results[3].Passed, "any error from the service passes GetJobFails");
    }

    [Fact]
    public async Task ANetworkFailureIsNotAnErrorFromTheService()
    {
        var results = await RunAllAsync(_ => throw new HttpRequestException("connection refused"));

        Assert.All(results, result => Assert.False(result.Passed));
        Assert.IsType<HttpRequestException>(results[3].Exception);
    }

    [Fact]
    public void ConfigureReturnsACopyAndLeavesTheGivenConfigurationAlone()
    {
        var config = new JobsClientConfig { Endpoint = new Uri("http://localhost") };

        var configured = JobsSmokeTests.All[0].Configure(config);

        Assert.NotSame(config, configured);
        Assert.Equal(config.Endpoint, configured.Endpoint);
    }

    private static async Task<List<SmithySmokeTestResult>> RunAllAsync(
        Func<HttpRequestMessage, HttpResponseMessage> respond
    )
    {
        using var httpClient = new HttpClient(new Handler(respond));
        var config = new JobsClientConfig { Endpoint = new Uri("http://localhost") };
        var results = new List<SmithySmokeTestResult>();
        foreach (var test in JobsSmokeTests.All)
        {
            results.Add(await test.RunAsync(config, c => new JobsClient(httpClient, c)));
        }
        return results;
    }

    private static HttpResponseMessage Service(HttpRequestMessage request) =>
        request.RequestUri!.AbsolutePath switch
        {
            "/jobs/job-1" => Json(HttpStatusCode.OK, """{"status":"done"}"""),
            "/jobs/missing" => Error(HttpStatusCode.NotFound, "JobNotFound"),
            "/jobs/expired" => Error(HttpStatusCode.BadRequest, "JobExpired"),
            _ => Error(HttpStatusCode.BadRequest, "InvalidJobId"),
        };

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Error(HttpStatusCode status, string code)
    {
        var response = Json(status, "{}");
        response.Headers.Add("X-Amzn-Errortype", code);
        return response;
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) => Task.FromResult(respond(request));
    }
}
