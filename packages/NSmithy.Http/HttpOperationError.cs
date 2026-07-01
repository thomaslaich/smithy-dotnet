using NSmithy.Core;

namespace NSmithy.Http;

public sealed class HttpOperationError(
    ShapeId id,
    int httpStatusCode,
    Func<SmithyHttpResponse, Exception> deserialize
)
{
    private readonly Func<SmithyHttpResponse, Exception> deserialize =
        deserialize ?? throw new ArgumentNullException(nameof(deserialize));

    public ShapeId Id { get; } = id;

    public int HttpStatusCode { get; } = httpStatusCode;

    public Exception Deserialize(SmithyHttpResponse response) => deserialize(response);
}
