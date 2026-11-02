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

    let response = client.SayHelloAsync({ Name = "world" }).GetAwaiter().GetResult()
    printfn "%s" response.Message
    0
