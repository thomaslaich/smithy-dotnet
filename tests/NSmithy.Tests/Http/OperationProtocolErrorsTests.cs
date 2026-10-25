using System.Net;
using System.Text;
using NSmithy.Core;
using NSmithy.Http;
using NSmithy.Protocols.AwsJson;

namespace NSmithy.Tests.Http;

public sealed class OperationProtocolErrorsTests
{
    private static readonly HttpOperationError[] Errors =
    [
        new(ShapeId.Parse("example#NotFound"), 404, _ => new NotFoundError()),
        new(ShapeId.Parse("example#Invalid"), 400, _ => new InvalidError()),
    ];

    [Fact]
    public void ACodeNamingAModeledErrorSelectsIt()
    {
        var error = Resolve(
            Response(HttpStatusCode.BadRequest, "{\"__type\":\"example#NotFound\"}")
        );

        Assert.IsType<NotFoundError>(error);
    }

    [Fact]
    public void ACodeNamingNoModeledErrorIsUnmodeledWhateverTheStatus()
    {
        var error = Resolve(
            Response(HttpStatusCode.BadRequest, "{\"__type\":\"ValidationException\"}")
        );

        Assert.Null(error);
    }

    [Fact]
    public void AResponseWithoutACodeFallsBackToTheStatus()
    {
        var error = Resolve(Response(HttpStatusCode.BadRequest, "{\"message\":\"bad\"}"));

        Assert.IsType<InvalidError>(error);
    }

    [Theory]
    [InlineData("{\"__type\":\"com.example#ValidationException\"}", "ValidationException")]
    [InlineData(
        "{\"__type\":\"ValidationException:http://internal.amazon.com/\"}",
        "ValidationException"
    )]
    [InlineData("{\"code\":\"InvalidInstanceID.NotFound\"}", "InvalidInstanceID.NotFound")]
    [InlineData("{\"message\":\"no code\"}", null)]
    [InlineData("<html>Service Unavailable</html>", null)]
    public void ReadErrorNameStripsNamespacesAndSuffixes(string body, string? expected)
    {
        var name = OperationProtocolErrors.ReadErrorName(
            Response(HttpStatusCode.BadRequest, body),
            AwsJsonProtocol.DeserializeErrorType
        );

        Assert.Equal(expected, name);
    }

    [Fact]
    public void ReadErrorNameIsNullWhenTheDiscriminatorCannotReadTheBody()
    {
        var name = OperationProtocolErrors.ReadErrorName(
            Response(HttpStatusCode.ServiceUnavailable, "<html>"),
            _ => throw new System.Xml.XmlException("not XML")
        );

        Assert.Null(name);
    }

    private static Exception? Resolve(SmithyHttpClientResponse response) =>
        OperationProtocolErrors.DeserializeModeledError(
            Errors,
            response,
            AwsJsonProtocol.DeserializeErrorType,
            requiresErrorDiscriminator: false,
            supportsHttpStatusErrorFallback: true
        );

    private static SmithyHttpClientResponse Response(HttpStatusCode status, string body) =>
        new(
            status,
            null,
            Encoding.UTF8.GetBytes(body),
            new Dictionary<string, IReadOnlyList<string>>(),
            new Dictionary<string, IReadOnlyList<string>>()
        );

    private sealed class NotFoundError : Exception;

    private sealed class InvalidError : Exception;
}
