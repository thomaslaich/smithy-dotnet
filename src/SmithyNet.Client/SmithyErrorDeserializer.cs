using SmithyNet.Http;

namespace SmithyNet.Client;

public delegate ValueTask<Exception?> SmithyErrorDeserializer(
    SmithyHttpResponse response,
    CancellationToken cancellationToken = default
);
