using NSmithy.Http;

namespace NSmithy.Client;

public interface IClientInterceptor
{
    void OnBeforeExecution(SmithyContext context) { }

    void OnBeforeSerialization(SmithyContext context, object? input) { }

    ValueTask<SmithyHttpRequest> OnBeforeSigningAsync(
        SmithyContext context,
        SmithyHttpRequest request,
        CancellationToken cancellationToken = default
    ) => ValueTask.FromResult(request);

    ValueTask<SmithyHttpRequest> OnBeforeTransmitAsync(
        SmithyContext context,
        SmithyHttpRequest request,
        CancellationToken cancellationToken = default
    ) => ValueTask.FromResult(request);

    void OnAfterTransmit(SmithyContext context, SmithyHttpResponse response) { }

    void OnAfterDeserialization(SmithyContext context, object? output) { }

    void OnAfterExecution(SmithyContext context) { }
}
