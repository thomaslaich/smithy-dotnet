open Example.Bookshelf
open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.DependencyInjection
open Bookshelf.Server.BookshelfHandler

[<EntryPoint>]
let main args =
    let builder = WebApplication.CreateBuilder(args)
    builder.Services.AddBookshelfServiceHandler<BookshelfHandler>() |> ignore

    let app = builder.Build()
    app.MapBookshelfService() |> ignore
    app.Run()
    0
