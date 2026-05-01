using System.Globalization;
using NSmithy.Codecs.Cbor;
using NSmithy.Core;
using NSmithy.Core.Annotations;

namespace NSmithy.Tests.Client;

public sealed class SmithyCborPayloadCodecTests
{
    [Fact]
    public void SerializeAndDeserializeRoundTripsSmithyShapes()
    {
        var payload = new ForecastRequest(
            "Zurich",
            DateTimeOffset.Parse("2026-04-23T10:15:30Z", CultureInfo.InvariantCulture),
            new ForecastValues([1, 2, 3])
        );

        var bytes = SmithyCborPayloadCodec.Default.Serialize(payload);
        var roundTrip = SmithyCborPayloadCodec.Default.Deserialize<ForecastRequest>(bytes);

        Assert.Equal(payload.City, roundTrip.City);
        Assert.Equal(payload.At, roundTrip.At);
        Assert.Equal(payload.Values.Values, roundTrip.Values.Values);
    }

    [Fact]
    public void DeserializeReadsNamedMembersFromCborMapsThroughWholeValueBinding()
    {
        var envelope = new ErrorEnvelope("example.weather#BadRequest", "bad city");

        var bytes = SmithyCborPayloadCodec.Default.Serialize(envelope);
        var roundTrip = SmithyCborPayloadCodec.Default.Deserialize<ErrorEnvelope>(bytes);

        Assert.Equal("example.weather#BadRequest", roundTrip.Type);
        Assert.Equal("bad city", roundTrip.Message);
    }

    [SmithyShape("example.weather#ForecastRequest", ShapeKind.Structure)]
    private sealed record class ForecastRequest(
        [property: SmithyMember("city", "smithy.api#String", IsRequired = true)] string City,
        [property: SmithyMember("at", "smithy.api#Timestamp", IsRequired = true)] DateTimeOffset At,
        [property: SmithyMember("values", "example.weather#ForecastValues", IsRequired = true)]
            ForecastValues Values
    );

    [SmithyShape("example.weather#ForecastValues", ShapeKind.List)]
    private sealed record class ForecastValues(
        [property: SmithyMember("member", "smithy.api#Integer")] IReadOnlyList<int> Values
    );

    [SmithyShape("example.weather#ErrorEnvelope", ShapeKind.Structure)]
    private sealed record class ErrorEnvelope(
        [property: SmithyMember("__type", "smithy.api#String", IsRequired = true)] string Type,
        [property: SmithyMember("message", "smithy.api#String")] string? Message
    );
}
