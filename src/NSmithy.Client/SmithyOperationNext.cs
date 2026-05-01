namespace NSmithy.Client;

public delegate Task<SmithyOperationResponse> SmithyOperationNext(
    SmithyOperationRequest request,
    CancellationToken cancellationToken = default
);
