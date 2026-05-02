using NSmithy.Http;

namespace NSmithy.Client;

public sealed record SmithyOperationResponse(
    string ServiceName,
    string OperationName,
    SmithyHttpResponse Response
);
