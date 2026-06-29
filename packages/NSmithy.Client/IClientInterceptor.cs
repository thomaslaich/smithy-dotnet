using NSmithy.Http;

namespace NSmithy.Client;

public interface IClientInterceptor
{
    void OnBeforeExecution(SmithyContext context) { }

    void OnBeforeSerialization(SmithyContext context, object? input) { }

    SmithyHttpRequest OnBeforeSigning(SmithyContext context, SmithyHttpRequest request) => request;

    SmithyHttpRequest OnBeforeTransmit(SmithyContext context, SmithyHttpRequest request) => request;

    void OnAfterTransmit(SmithyContext context, SmithyHttpResponse response) { }

    void OnAfterDeserialization(SmithyContext context, object? output) { }

    void OnAfterExecution(SmithyContext context) { }
}
