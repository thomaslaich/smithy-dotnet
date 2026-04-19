namespace SmithyNet.Server;

public delegate Task<SmithyServerResponse> SmithyServerOperationNext(
    SmithyServerRequest request,
    CancellationToken cancellationToken = default
);
