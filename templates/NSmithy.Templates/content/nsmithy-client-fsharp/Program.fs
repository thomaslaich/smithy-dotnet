open System
open Example.Hello
//#if (IsGrpc)
open NSmithy.Protocols.Grpc
//#endif

[<EntryPoint>]
let main args =
    //#if (IsGrpc)
    let endpoint = args |> Array.tryHead |> Option.defaultValue "http://localhost:5001"

    // Native gRPC: setting Protocol selects gRPC and configures the HTTP/2 HttpClient it requires.
    use client =
        new HelloServiceClient(Uri endpoint, HelloServiceClientConfig(Protocol = GrpcProtocol()))
    //#else
    let endpoint = args |> Array.tryHead |> Option.defaultValue "http://localhost:5000"
    use client = new HelloServiceClient(Uri endpoint)
    //#endif

    // The server validates the input, so the operation returns a Result.
    match client.SayHelloAsync({ Name = "world" }).GetAwaiter().GetResult() with
    | Ok response ->
        printfn "%s" response.Message
        0
    | Error(SayHelloError.ValidationException error) ->
        eprintfn "Rejected: %s" error.Message
        1
