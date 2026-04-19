namespace SmithyNet.Server;

public delegate Task<SmithyServerResponse> SmithyServerOperationHandler(
    SmithyServerRequest request,
    CancellationToken cancellationToken = default
);
