using NSmithy.Http;

namespace NSmithy.Client;

/// <summary>Signs a serialized request with a resolved identity.</summary>
public interface ISmithySigner
{
    ValueTask<SmithyHttpRequest> SignAsync(
        SmithyContext context,
        SmithyHttpRequest request,
        ISmithyIdentity identity,
        CancellationToken cancellationToken = default
    );
}
