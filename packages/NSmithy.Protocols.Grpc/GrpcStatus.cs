using System.Net;

namespace NSmithy.Protocols.Grpc;

/// <summary>
/// The canonical gRPC status codes carried in the <c>grpc-status</c> trailer. gRPC always returns
/// HTTP 200; success and failure are distinguished by this code, not the HTTP status. NSmithy maps
/// modeled-error HTTP status codes onto these so the gRPC and HTTP/REST surfaces share one error
/// pipeline.
/// </summary>
public enum GrpcStatus
{
    Ok = 0,
    Cancelled = 1,
    Unknown = 2,
    InvalidArgument = 3,
    DeadlineExceeded = 4,
    NotFound = 5,
    AlreadyExists = 6,
    PermissionDenied = 7,
    ResourceExhausted = 8,
    FailedPrecondition = 9,
    Aborted = 10,
    OutOfRange = 11,
    Unimplemented = 12,
    Internal = 13,
    Unavailable = 14,
    DataLoss = 15,
    Unauthenticated = 16,
}

public static class GrpcStatusMapping
{
    /// <summary>Maps a modeled error's HTTP status code to the closest gRPC status code.</summary>
    public static GrpcStatus FromHttpStatus(int statusCode) =>
        (HttpStatusCode)statusCode switch
        {
            HttpStatusCode.BadRequest => GrpcStatus.InvalidArgument,
            HttpStatusCode.Unauthorized => GrpcStatus.Unauthenticated,
            HttpStatusCode.Forbidden => GrpcStatus.PermissionDenied,
            HttpStatusCode.NotFound => GrpcStatus.NotFound,
            HttpStatusCode.Conflict => GrpcStatus.AlreadyExists,
            HttpStatusCode.TooManyRequests => GrpcStatus.ResourceExhausted,
            HttpStatusCode.NotImplemented => GrpcStatus.Unimplemented,
            HttpStatusCode.ServiceUnavailable => GrpcStatus.Unavailable,
            HttpStatusCode.GatewayTimeout => GrpcStatus.DeadlineExceeded,
            _ => (int)(HttpStatusCode)statusCode >= 500 ? GrpcStatus.Internal : GrpcStatus.Unknown,
        };
}
