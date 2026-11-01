module NSmithy.FSharp.Tests.ServerTests

open System
open System.Threading.Tasks
open Example.Kitchen
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.TestHost
open NSmithy.Http
open NSmithy.Protocols.RestJson
open NSmithy.Protocols.RpcV2Cbor
open NSmithy.Protocols.RpcV2Json
open Xunit

/// Serves orders, and is out of stock of any dish named "sold out".
type private Kitchen() =
    interface IKitchenServiceHandler with
        member _.OrderAsync(input, _) =
            task {
                if input.Dish.Name = "sold out" then
                    raise (OutOfStock(Ingredient.CHEESE, errorMessage = "gone"))

                return
                    { Receipt =
                        Receipt.Paid
                            { Amount = decimal input.Dish.Price * decimal input.Guests
                              Currency = "CHF" } }
            }

        member _.PingAsync(_, _) = Task.FromResult(PingOutput())

        member _.CloseTableAsync(_, _) = Task.FromResult(CloseTableOutput())

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

        let! output = client.OrderAsync(order "pizza")

        Assert.Equal(Receipt.Paid { Amount = 19M; Currency = "CHF" }, output.Receipt)
    }

[<Theory>]
[<MemberData(nameof protocols)>]
let ``A modeled error a handler raises reaches the client``
    (served: KitchenServiceProtocols, protocol: IProtocol)
    : Task =
    task {
        use app = startServer served
        use client = client app protocol

        let! error = Assert.ThrowsAsync<OutOfStock>(fun () -> client.OrderAsync(order "sold out") :> Task)

        Assert.Equal(Ingredient.CHEESE, error.Item)
        Assert.Equal(Some "gone", error.ErrorMessage)
    }

[<Fact>]
let ``The server validates input before the handler runs`` () : Task =
    task {
        use app = startServer KitchenServiceProtocols.RestJson1
        use client = client app (RestJson1Protocol())

        let unknownKey =
            { (order "pizza").Dish with
                Tags = Some(Map [ "pineapple", Some 1 ]) }

        let! error =
            Assert.ThrowsAsync<NSmithy.Core.Validation.ValidationException>(fun () ->
                client.OrderAsync { order "pizza" with Dish = unknownKey } :> Task)

        Assert.Contains("/dish/tags", error.Message)
    }
