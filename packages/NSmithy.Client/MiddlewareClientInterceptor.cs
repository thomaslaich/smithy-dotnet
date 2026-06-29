using System.Net;
using NSmithy.Http;

namespace NSmithy.Client;

internal sealed class MiddlewareClientInterceptor(ISmithyClientMiddleware middleware)
    : IClientInterceptor
{
    private readonly ISmithyClientMiddleware middleware =
        middleware ?? throw new ArgumentNullException(nameof(middleware));

    public async ValueTask<SmithyHttpRequest> OnBeforeTransmitAsync(
        SmithyContext context,
        SmithyHttpRequest request,
        CancellationToken cancellationToken = default
    )
    {
        SmithyHttpRequest? modifiedRequest = null;
        var operationRequest = new SmithyOperationRequest(
            context.Get(SmithyContextKeys.ServiceName),
            context.Get(SmithyContextKeys.OperationName),
            request
        );

        await middleware
            .InvokeAsync(
                operationRequest,
                (nextRequest, _) =>
                {
                    modifiedRequest = nextRequest.Request;
                    return Task.FromResult(
                        new SmithyOperationResponse(
                            nextRequest.ServiceName,
                            nextRequest.OperationName,
                            new SmithyHttpResponse(
                                HttpStatusCode.OK,
                                "OK",
                                [],
                                new Dictionary<string, IReadOnlyList<string>>(
                                    StringComparer.OrdinalIgnoreCase
                                ),
                                new Dictionary<string, IReadOnlyList<string>>(
                                    StringComparer.OrdinalIgnoreCase
                                )
                            )
                        )
                    );
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        return modifiedRequest ?? request;
    }
}
