open System.Threading.Tasks
open Example.Hello
open Microsoft.AspNetCore.Builder
//#if (IsGrpc)
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Server.Kestrel.Core
//#endif
open Microsoft.Extensions.DependencyInjection
//#if (WithDocs)
open NSmithy.Server.AspNetCore.Docs
//#endif

type HelloHandler() =
    interface IHelloServiceHandler with
        member _.SayHelloAsync(input, _) =
            Task.FromResult { Message = $"Hello, {input.Name}!" }

[<EntryPoint>]
let main args =
    let builder = WebApplication.CreateBuilder(args)
    //#if (IsGrpc)

    // Native gRPC: the generated server speaks the gRPC wire format itself via
    // NSmithy.Protocols.Grpc, so no Grpc.AspNetCore, Grpc.Tools, or protoc is needed.
    builder.WebHost.ConfigureKestrel(fun options ->
        options.ListenLocalhost(5000, fun o -> o.Protocols <- HttpProtocols.Http1)
        options.ListenLocalhost(5001, fun o -> o.Protocols <- HttpProtocols.Http2))
    |> ignore

    //#endif
    builder.Services.AddHelloServiceHandler<HelloHandler>() |> ignore

    let app = builder.Build()
    //#if (WithDocs && IsRestJson1)
    app.MapSmithyOpenApi() |> ignore
    app.MapSmithyDocs() |> ignore
    //#elif (WithDocs)
    app.MapSmithyDocs() |> ignore
    //#endif
    app.MapHelloService() |> ignore
    app.Run()
    0
