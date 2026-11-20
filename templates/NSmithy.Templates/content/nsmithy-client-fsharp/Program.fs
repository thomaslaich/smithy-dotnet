open System
open Example.Hello
//#if (IsGrpc)
open NSmithy.Protocols.Grpc
//#endif

// The model declares ValidationException on SayHello, so the operation returns a Result.
let run (client: HelloServiceClient) =
    task {
        match! client.SayHelloAsync({ Name = "world" }) with
        | Ok response ->
            printfn "%s" response.Message
            return 0
        | Error(SayHelloError.ValidationException error) ->
            eprintfn "Rejected: %s" error.Message
            return 1
    }

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

    (run client).GetAwaiter().GetResult()
