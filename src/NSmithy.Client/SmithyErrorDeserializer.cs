using NSmithy.Http;

namespace NSmithy.Client;

public delegate ValueTask<Exception?> SmithyErrorDeserializer(
    SmithyHttpResponse response,
    CancellationToken cancellationToken = default
);
