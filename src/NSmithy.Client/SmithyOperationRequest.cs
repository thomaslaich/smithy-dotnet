using NSmithy.Http;

namespace NSmithy.Client;

public sealed record SmithyOperationRequest(
    string ServiceName,
    string OperationName,
    SmithyHttpRequest Request
);
