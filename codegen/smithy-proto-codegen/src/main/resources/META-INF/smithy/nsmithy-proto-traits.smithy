$version: "2"

namespace nsmithy.proto

/// Marks an operation's output as gRPC server-streamed.
/// The proto codegen emits `stream` on the rpc return type.
/// The C# codegen generates IAsyncEnumerable<T> on the handler/client.
@trait(selector: "operation")
structure grpcServerStream {}

/// Marks an operation's input as gRPC client-streamed.
/// The proto codegen emits `stream` on the rpc request type.
/// The C# codegen generates IAsyncEnumerable<T> on the handler/client.
@trait(selector: "operation")
structure grpcClientStream {}
