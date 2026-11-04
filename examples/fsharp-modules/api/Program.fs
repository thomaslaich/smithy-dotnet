open Example.Shop
open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.DependencyInjection
open NSmithy.Examples.FSharpModules.Orders
open NSmithy.Examples.FSharpModules.Payments

[<EntryPoint>]
let main args =
    let builder = WebApplication.CreateBuilder(args)
    builder.Services.AddShopService() |> ignore

    // Each module implements its own operation's handler interface.
    builder.Services.AddSingleton<IPlaceOrderHandler, PlaceOrderHandler>() |> ignore

    builder.Services.AddSingleton<ICapturePaymentHandler, CapturePaymentHandler>()
    |> ignore

    let app = builder.Build()
    app.MapShopService() |> ignore
    app.Run()
    0
