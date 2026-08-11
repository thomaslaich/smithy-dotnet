using NSmithy.Core.Serde;

namespace NSmithy.Http;

/// <summary>
/// A protocol bound to a single service. Produced from a <see cref="ServiceSchema"/>; hands out
/// per-operation protocols. Service-level concerns (e.g. deriving the rpcv2Cbor request path from
/// the service shape name, and — in future — auth and endpoint resolution) live here, set up once.
/// </summary>
/// <remarks>
/// The two call sides are handed out separately, mirroring the split between
/// <see cref="IClientOperationProtocol{TInput, TOutput}"/> and
/// <see cref="IServerOperationProtocol{TInput, TOutput}"/>. An implementation may well answer both
/// with one object, but it is told which side it is building for, so work only one side does — input
/// validation, for instance — is not compiled into the other. Generated clients and generated server
/// endpoints already bind separately, so this costs nothing beyond the call they each already make.
/// </remarks>
public interface IServiceProtocol
{
    IClientOperationProtocol<TInput, TOutput> ForClientOperation<TInput, TOutput>(
        OperationSchema<TInput, TOutput> operation
    );

    IServerOperationProtocol<TInput, TOutput> ForServerOperation<TInput, TOutput>(
        OperationSchema<TInput, TOutput> operation
    );
}
