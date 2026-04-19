using SmithyNet.Http;

namespace SmithyNet.Client;

public sealed record SmithyOperationResponse(
    string ServiceName,
    string OperationName,
    SmithyHttpResponse Response
);
