using NSmithy.Contracts;

namespace NSmithy.Tests.MSBuild;

public sealed class SmithyValidationEventsTests
{
    [Fact]
    public void ParsesValidationEventsCsv()
    {
        const string csv = """
            severity,id,shape,file,line,column,message,hint,suppressionReason
            "ERROR","Model.UnresolvedTrait","a.b#Invalid","/models/main.smithy",3,1,"Unable to resolve trait `aws.protocols#rpcv2Cbor`.","",""
            "WARNING","Model","","model/other.smithy",5,5,"Use statement refers to undefined shape","",""

            """;

        var events = SmithyValidationEvents.ParseCsv(csv);

        Assert.Equal(
            [
                new SmithyValidationEvent(
                    "ERROR",
                    "Model.UnresolvedTrait",
                    "Unable to resolve trait `aws.protocols#rpcv2Cbor`.",
                    Shape: "a.b#Invalid",
                    File: "/models/main.smithy",
                    Line: 3,
                    Column: 1
                ),
                new SmithyValidationEvent(
                    "WARNING",
                    "Model",
                    "Use statement refers to undefined shape",
                    File: "model/other.smithy",
                    Line: 5,
                    Column: 5
                ),
            ],
            events
        );
    }

    [Fact]
    public void ParsesQuotedFieldsAndMissingLocation()
    {
        const string csv =
            "severity,id,shape,file,line,column,message,hint,suppressionReason\r\n"
            + "\"DANGER\",\"SomeValidator\",\"a.b#Foo\",\"N/A\",0,0,"
            + "\"Says \"\"hi\"\", then\nbreaks, a line\",\"Try this\",\"\"\r\n";

        var danger = Assert.Single(SmithyValidationEvents.ParseCsv(csv));

        Assert.Equal(
            new SmithyValidationEvent(
                "DANGER",
                "SomeValidator",
                "Says \"hi\", then\nbreaks, a line",
                Shape: "a.b#Foo",
                Hint: "Try this"
            ),
            danger
        );
    }

    [Fact]
    public void LooksUpColumnsByName()
    {
        const string csv = """
            id,severity,message,file,line,column
            "Model","ERROR","Broken","main.smithy",2,4
            """;

        var error = Assert.Single(SmithyValidationEvents.ParseCsv(csv));

        Assert.Equal(
            new SmithyValidationEvent(
                "ERROR",
                "Model",
                "Broken",
                File: "main.smithy",
                Line: 2,
                Column: 4
            ),
            error
        );
    }

    [Fact]
    public void ReturnsNoEventsForEmptyOutput()
    {
        Assert.Empty(SmithyValidationEvents.ParseCsv(""));
    }
}
