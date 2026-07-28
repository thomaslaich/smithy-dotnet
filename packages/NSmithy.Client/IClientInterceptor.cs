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

    void OnAfterTransmit(SmithyContext context, SmithyHttpClientResponse response) { }

    void OnAfterDeserialization(SmithyContext context, object? output) { }

    /// <summary>
    /// Runs once per execution, after success or failure. <paramref name="exception"/> is null
    /// when the operation succeeded; otherwise it is the exception that will propagate to the
    /// caller (a modeled error, a <see cref="SmithyClientException"/>, or a transport failure).
    /// </summary>
    void OnAfterExecution(SmithyContext context, Exception? exception) { }
}
