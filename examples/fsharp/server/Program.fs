open Example.Bookshelf
open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.DependencyInjection
open Bookshelf.Server

[<EntryPoint>]
let main args =
    let builder = WebApplication.CreateBuilder(args)

    builder.Services.AddBookshelfServiceHandler(BookshelfHandler.create ())
    |> ignore

    let app = builder.Build()
    app.MapBookshelfService() |> ignore
    app.Run()
    0
