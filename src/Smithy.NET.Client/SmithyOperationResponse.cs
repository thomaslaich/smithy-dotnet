using Smithy.NET.Http;

namespace Smithy.NET.Client;

public sealed record SmithyOperationResponse(
    string ServiceName,
    string OperationName,
    SmithyHttpResponse Response
);
