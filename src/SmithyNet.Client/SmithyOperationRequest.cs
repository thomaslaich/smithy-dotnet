using SmithyNet.Http;

namespace SmithyNet.Client;

public sealed record SmithyOperationRequest(
    string ServiceName,
    string OperationName,
    SmithyHttpRequest Request
);
