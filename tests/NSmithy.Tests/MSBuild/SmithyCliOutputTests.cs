using NSmithy.Contracts;

namespace NSmithy.Tests.MSBuild;

public sealed class SmithyCliOutputTests
{
    [Fact]
    public void ParsesValidationEventsFromCliOutput()
    {
        string[] output =
        [
            "",
            "──  WARNING  ───────────────────────────────────────────────────────────── Model",
            "File:  model/weather.smithy:5:5",
            "",
            "5| use aws.protocols#rpcv2Cbor",
            " |     ^",
            "",
            "Use statement refers to undefined shape: aws.protocols#rpcv2Cbor",
            "",
            "",
            "──  ERROR  ─────────────────────────────────────────────── Model.UnresolvedTrait",
            "Shape: example.weather#Weather",
            "File:  model/weather.smithy:12:1",
            "",
            "12| @rpcv2Cbor",
            "  | ^",
            "··|",
            "14| service Weather {",
            "",
            "Unable to resolve trait `aws.protocols#rpcv2Cbor`. If this is a custom trait,",
            "then it must be defined before it can be used in a model.",
            "",
            "FAILURE: Validated 291 shapes (ERROR: 1, WARNING: 1)",
        ];

        var events = SmithyCliOutput.Parse(output);

        Assert.Equal(
            [
                new SmithyValidationEvent(
                    "WARNING",
                    "Model",
                    "Use statement refers to undefined shape: aws.protocols#rpcv2Cbor",
                    File: "model/weather.smithy",
                    Line: 5,
                    Column: 5,
                    Frame: "5| use aws.protocols#rpcv2Cbor\n |     ^"
                ),
                new SmithyValidationEvent(
                    "ERROR",
                    "Model.UnresolvedTrait",
                    "Unable to resolve trait `aws.protocols#rpcv2Cbor`. If this is a custom trait, then it must be defined before it can be used in a model.",
                    Shape: "example.weather#Weather",
                    File: "model/weather.smithy",
                    Line: 12,
                    Column: 1,
                    Frame: "12| @rpcv2Cbor\n  | ^\n··|\n14| service Weather {"
                ),
            ],
            events
        );
    }

    [Fact]
    public void ParsesEventWithoutLocationAndIgnoresUnrelatedOutput()
    {
        string[] output =
        [
            "Unexpected CLI argument: --bogus",
            "\u001b[31m──  DANGER  ──────────────────── SomeValidator\u001b[0m",
            "Shape: example#Foo",
            "Something dangerous happened.",
        ];

        var events = SmithyCliOutput.Parse(output);

        var danger = Assert.Single(events);
        Assert.Equal("DANGER", danger.Severity);
        Assert.Equal("SomeValidator", danger.EventId);
        Assert.Equal("example#Foo", danger.Shape);
        Assert.Null(danger.File);
        Assert.Null(danger.Frame);
        Assert.Equal("Something dangerous happened.", danger.Message);
    }
}
