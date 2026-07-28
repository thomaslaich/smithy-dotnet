using NSmithy.Core;

namespace NSmithy.Http;

public sealed class HttpOperationError(
    ShapeId id,
    int httpStatusCode,
    Func<SmithyHttpClientResponse, Exception> deserialize
)
{
    private readonly Func<SmithyHttpClientResponse, Exception> deserialize =
        deserialize ?? throw new ArgumentNullException(nameof(deserialize));

    public ShapeId Id { get; } = id;

    public int HttpStatusCode { get; } = httpStatusCode;

    public Exception Deserialize(SmithyHttpClientResponse response) => deserialize(response);
}
