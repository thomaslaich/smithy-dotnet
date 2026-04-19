namespace SmithyNet.Server;

public interface ISmithyServerMiddleware
{
    Task<SmithyServerResponse> InvokeAsync(
        SmithyServerRequest request,
        SmithyServerOperationNext nextOperation,
        CancellationToken cancellationToken = default
    );
}
