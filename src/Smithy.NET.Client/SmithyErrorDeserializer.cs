using Smithy.NET.Http;

namespace Smithy.NET.Client;

public delegate ValueTask<Exception?> SmithyErrorDeserializer(
    SmithyHttpResponse response,
    CancellationToken cancellationToken = default
);
