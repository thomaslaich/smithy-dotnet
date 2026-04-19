using Smithy.NET.Http;

namespace Smithy.NET.Client;

public sealed record SmithyOperationRequest(
    string ServiceName,
    string OperationName,
    SmithyHttpRequest Request
);
