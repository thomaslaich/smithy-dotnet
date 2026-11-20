module NSmithy.FSharp.Tests.ServerTests

open System
open System.Threading.Tasks
open Example.Kitchen
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.DependencyInjection
open NSmithy.Http
open NSmithy.Protocols.RestJson
open NSmithy.Protocols.RpcV2Cbor
open NSmithy.Protocols.RpcV2Json
open Xunit

/// Serves orders, is out of stock of any dish named "sold out", and closed for one named "closed".
type private Kitchen() =
    interface IKitchenServiceHandler with
        member _.OrderAsync(input, _) =
            task {
                match input.Dish.Name with
                | "sold out" ->
                    return Error(OrderError.OutOfStock(OutOfStock(Ingredient.CHEESE, errorMessage = "gone")))
                | "closed" -> return raise (Example.Staff.KitchenClosed(errorMessage = "closed"))
                | _ ->
                    return
                        Ok
                            { Receipt =
                                Receipt.Paid
                                    { Amount = decimal input.Dish.Price * decimal input.Guests
                                      Currency = "CHF" } }
            }

        member _.PingAsync(_, _) = Task.FromResult(PingOutput())

        member _.CloseTableAsync(_, _) = Task.FromResult(Ok(CloseTableOutput()))

/// Serves the kitchen over one protocol. RPC v2 CBOR and RPC v2 JSON share their routes, so one
/// endpoint builder cannot serve both.
let private startServer (protocols: KitchenServiceProtocols) =
    let builder = WebApplication.CreateBuilder()
    builder.WebHost.UseTestServer() |> ignore
    builder.Services.AddKitchenServiceHandler<Kitchen>() |> ignore
    let app = builder.Build()
    app.MapKitchenService(protocols) |> ignore
    app.StartAsync().Wait()
    app

let private client (app: WebApplication) (protocol: IProtocol) =
    let http = app.GetTestClient()
    new KitchenClient(http, KitchenClientConfig(Endpoint = http.BaseAddress, Protocol = protocol))

let private order name =
    { Table = "7"
      ClientToken = None
      Dish =
        { Name = name
          Ingredients = [ Ingredient.CHEESE ]
          Sides = None
          Price = 9.5f
          Spice = Some Spice.HOT
          Tags = Some(Map [ "BACON", Some 1; "cheese", None ])
          Extras = []
          Blob = None
          MadeAt = None
          Big = None
          Dec = None
          Offset = 0L
          Type = None
          ToStringValue = None }
      Notes = Some [ Some "n"; None ]
      Seating = Seating.BAR
      Guests = 2
      Tip = None }

let protocols: obj[] list =
    [ [| KitchenServiceProtocols.RestJson1; RestJson1Protocol() :> IProtocol |]
      [| KitchenServiceProtocols.SimpleRestJson
         SimpleRestJsonProtocol() :> IProtocol |]
      [| KitchenServiceProtocols.RpcV2Cbor; RpcV2CborProtocol() :> IProtocol |]
      [| KitchenServiceProtocols.RpcV2Json; RpcV2JsonProtocol() :> IProtocol |] ]

[<Theory>]
[<MemberData(nameof protocols)>]
let ``An F# client calls an F# server`` (served: KitchenServiceProtocols, protocol: IProtocol) : Task =
    task {
        use app = startServer served
        use client = client app protocol

        let! result = client.OrderAsync(order "pizza")

        match result with
        | Ok output -> Assert.Equal(Receipt.Paid { Amount = 19M; Currency = "CHF" }, output.Receipt)
        | Error error -> failwith $"Unexpected error: {error}"
    }

[<Theory>]
[<MemberData(nameof protocols)>]
let ``A modeled error a handler returns reaches the client``
    (served: KitchenServiceProtocols, protocol: IProtocol)
    : Task =
    task {
        use app = startServer served
        use client = client app protocol

        let! result = client.OrderAsync(order "sold out")

        match result with
        | Error(KitchenCallError.Operation(OrderError.OutOfStock error)) ->
            Assert.Equal(Ingredient.CHEESE, error.Item)
            Assert.Equal(Some "gone", error.ErrorMessage)
        | other -> failwith $"Unexpected result: {other}"
    }

[<Theory>]
[<MemberData(nameof protocols)>]
let ``A service error a handler raises reaches the client``
    (served: KitchenServiceProtocols, protocol: IProtocol)
    : Task =
    task {
        use app = startServer served
        use client = client app protocol

        let! result = client.OrderAsync(order "closed")

        match result with
        | Error(KitchenCallError.Service(KitchenError.KitchenClosed error)) ->
            Assert.Equal(Some "closed", error.ErrorMessage)
        | other -> failwith $"Unexpected result: {other}"
    }

[<Fact>]
let ``The server validates input before the handler runs`` () : Task =
    task {
        use app = startServer KitchenServiceProtocols.RestJson1
        use client = client app (RestJson1Protocol())

        let unknownKey =
            { (order "pizza").Dish with
                Tags = Some(Map [ "pineapple", Some 1 ]) }

        let! result = client.OrderAsync { order "pizza" with Dish = unknownKey }

        match result with
        | Error(KitchenCallError.Operation(OrderError.ValidationException error)) ->
            Assert.Contains("/dish/tags", error.Message)
            Assert.Equal("/dish/tags", (Seq.exactlyOne error.FieldList).Path)
        | other -> failwith $"Unexpected result: {other}"
    }

/// A handler that charges 1 CHF for any order.
let private paidHandler () =
    { new IKitchenServiceHandler with
        member _.OrderAsync(_, _) =
            Task.FromResult(Ok { Receipt = Receipt.Paid { Amount = 1M; Currency = "CHF" } })

        member _.PingAsync(_, _) = Task.FromResult(PingOutput())
        member _.CloseTableAsync(_, _) = Task.FromResult(Ok(CloseTableOutput())) }

/// Serves the kitchen over RestJson1 with the handler `register` adds, and orders twice.
let private orderTwice (register: IServiceCollection -> unit) : Task =
    task {
        let builder = WebApplication.CreateBuilder()
        builder.WebHost.UseTestServer() |> ignore
        register builder.Services
        use app = builder.Build()
        app.MapKitchenService(KitchenServiceProtocols.RestJson1) |> ignore
        do! app.StartAsync()
        use client = client app (RestJson1Protocol())

        for _ in 1..2 do
            let! result = client.OrderAsync(order "pizza")

            match result with
            | Ok output -> Assert.Equal(Receipt.Paid { Amount = 1M; Currency = "CHF" }, output.Receipt)
            | Error error -> failwith $"Unexpected error: {error}"
    }

[<Fact>]
let ``An object expression registered as the handler serves requests`` () : Task =
    orderTwice (fun services -> services.AddKitchenServiceHandler(paidHandler ()) |> ignore)

[<Fact>]
let ``A handler factory runs once by default`` () : Task =
    task {
        let created = ref 0

        do!
            orderTwice (fun services ->
                services.AddKitchenServiceHandler(fun _ ->
                    created.Value <- created.Value + 1
                    paidHandler ())
                |> ignore)

        Assert.Equal(1, created.Value)
    }

[<Fact>]
let ``A scoped handler factory runs once per request`` () : Task =
    task {
        let created = ref 0

        do!
            orderTwice (fun services ->
                services.AddKitchenServiceHandler(
                    (fun _ ->
                        created.Value <- created.Value + 1
                        paidHandler ()),
                    ServiceLifetime.Scoped
                )
                |> ignore)

        Assert.Equal(2, created.Value)
    }

[<Fact>]
let ``A scoped handler is shared within a request only`` () =
    let services = ServiceCollection()
    services.AddKitchenServiceHandler<Kitchen>(ServiceLifetime.Scoped) |> ignore
    use provider = services.BuildServiceProvider(validateScopes = true)
    use first = provider.CreateScope()
    use second = provider.CreateScope()
    let handler = first.ServiceProvider.GetRequiredService<IKitchenServiceHandler>()

    Assert.Same(handler, first.ServiceProvider.GetRequiredService<IKitchenServiceHandler>())
    Assert.NotSame(handler, second.ServiceProvider.GetRequiredService<IKitchenServiceHandler>())
