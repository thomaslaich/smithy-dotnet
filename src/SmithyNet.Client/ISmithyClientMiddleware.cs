namespace SmithyNet.Client;

public interface ISmithyClientMiddleware
{
    Task<SmithyOperationResponse> InvokeAsync(
        SmithyOperationRequest request,
        SmithyOperationNext nextOperation,
        CancellationToken cancellationToken = default
    );
}
