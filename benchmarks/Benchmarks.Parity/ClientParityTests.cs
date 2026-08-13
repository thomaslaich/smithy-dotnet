using Bench.Clients;
using Bench.Hosting;

namespace Bench.Parity;

/// <summary>
/// The client-side half of the fairness gate.
/// </summary>
/// <remarks>
/// The server gate pins stacks by the responses they return. This pins clients by
/// the requests they emit. Without it, "client A is faster than client B" could
/// simply mean client A omits a header, skips a query parameter, or sends a
/// smaller body.
/// <para>
/// Every client runs against the same reference server, so the server is a
/// constant and cannot bias the comparison. Two things are asserted: that the
/// bytes each client puts on the wire agree, and that each parses the response
/// into the same values.
/// </para>
/// </remarks>
public sealed class ClientParityTests
{
    /// <summary>The client whose requests define the expected contract.</summary>
    private const string Reference = BenchClientFactory.NSmithy;

    public static TheoryData<string> Scenarios
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var scenario in BenchClientScenarios.All)
                data.Add(scenario.Name);

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Scenarios))]
    public async Task AllClientsEmitTheSameRequest(string scenario)
    {
        var invoke = BenchClientScenarios.ByName(scenario).Invoke;

        await using var server = await BenchStacks.StartNSmithyAsync();

        CapturedRequest? expectedRequest = null;
        string? expectedResult = null;

        foreach (var clientName in BenchClientFactory.Names.OrderByDescending(n => n == Reference))
        {
            using var recorder = new RecordingHandler(server.CreateHandler());
            await using var client = BenchClientFactory.Create(clientName, recorder);

            var result = await invoke(client);

            Assert.Single(recorder.Captures);
            var actual = recorder.Captures[0];

            if (expectedRequest is null)
            {
                expectedRequest = actual;
                expectedResult = result;
                continue;
            }

            Assert.Equal(expectedRequest.Method, actual.Method);
            Assert.Equal(expectedRequest.PathAndQuery, actual.PathAndQuery);
            Assert.Equal(expectedRequest.Headers, actual.Headers);
            Assert.Equal(expectedRequest.BodyLength, actual.BodyLength);
            Assert.Equal(expectedRequest.BodySha256, actual.BodySha256);

            // A client that emitted the right bytes but misparsed the response
            // would still be wrong, and would look fast doing it.
            Assert.Equal(expectedResult, result);
        }

        Assert.NotNull(expectedRequest);
    }

    /// <summary>
    /// Guards the scenario set itself: a call that quietly failed would satisfy
    /// the equality assertions above while measuring nothing.
    /// </summary>
    [Theory]
    [MemberData(nameof(Scenarios))]
    public async Task ReferenceClientSucceeds(string scenario)
    {
        await using var server = await BenchStacks.StartNSmithyAsync();
        using var handler = server.CreateHandler();
        await using var client = BenchClientFactory.Create(Reference, handler);

        var result = await BenchClientScenarios.ByName(scenario).Invoke(client);

        Assert.False(string.IsNullOrWhiteSpace(result));
    }
}
