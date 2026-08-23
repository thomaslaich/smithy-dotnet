using Bench.Hosting;

namespace Bench.Parity;

public sealed class GrpcParityTests
{
    public static IEnumerable<object[]> Scenarios =>
        GrpcBenchScenarios.All.Select(scenario => new object[] { scenario });

    [Theory]
    [MemberData(nameof(Scenarios))]
    public async Task ClientsEmitTheSameRequest(GrpcBenchScenario scenario)
    {
        var grpcNetRequest = await CaptureClientRequest(GrpcBenchStacks.GrpcNet, scenario);
        var nsmithyRequest = await CaptureClientRequest(GrpcBenchStacks.NSmithy, scenario);

        Assert.Equal(scenario.MethodPath, grpcNetRequest.MethodPath);
        Assert.Equal(grpcNetRequest.MethodPath, nsmithyRequest.MethodPath);
        Assert.Equal(scenario.RequestBody, grpcNetRequest.Body);
        Assert.Equal(grpcNetRequest.Body, nsmithyRequest.Body);
    }

    [Theory]
    [MemberData(nameof(Scenarios))]
    public async Task ServersEmitTheSameResponse(GrpcBenchScenario scenario)
    {
        var grpcNetResponse = await CaptureServerResponse(GrpcBenchStacks.GrpcNet, scenario);
        var nsmithyResponse = await CaptureServerResponse(GrpcBenchStacks.NSmithy, scenario);

        Assert.Equal("0", grpcNetResponse.Status);
        Assert.Equal(grpcNetResponse.Status, nsmithyResponse.Status);
        Assert.Equal(scenario.ResponseBody, grpcNetResponse.Body);
        Assert.Equal(grpcNetResponse.Body, nsmithyResponse.Body);
    }

    private static async Task<CapturedGrpcRequest> CaptureClientRequest(
        string stack,
        GrpcBenchScenario scenario
    )
    {
        var handler = new GrpcCannedResponseHandler(scenario, record: true);
        await using var client = GrpcBenchClient.Create(stack, scenario, handler);
        _ = await client.InvokeAsync();
        return Assert.Single(handler.Captures);
    }

    private static async Task<(string Status, byte[] Body)> CaptureServerResponse(
        string stack,
        GrpcBenchScenario scenario
    )
    {
        using var server = GrpcBenchStacks.Start(stack);
        using var request = scenario.CreateRequest();
        using var response = await server.Client.SendAsync(request);
        var body = await response.Content.ReadAsByteArrayAsync();
        var status =
            response.TrailingHeaders.GetValues("grpc-status").SingleOrDefault()
            ?? response.Headers.GetValues("grpc-status").Single();
        return (status, body);
    }
}
