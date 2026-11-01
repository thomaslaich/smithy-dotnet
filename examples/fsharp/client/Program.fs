open System
open Example.Bookshelf
open NSmithy.Core.Validation


let describe (book: Book) =
    let status =
        match book.Status with
        | Status.OnShelf -> "on the shelf"
        | Status.LentTo reader -> $"lent to {reader}"
        | _ -> "somewhere this client does not know about"

    let pages =
        book.Pages |> Option.map (sprintf ", %d pages") |> Option.defaultValue ""

    $"{book.Title} by {book.Author} ({book.Genre.Value}{pages}), {status}"

let run (endpoint: string) =
    task {
        use client = new BookshelfClient(Uri endpoint)

        let! dune =
            client.AddBookAsync
                { Title = "Dune"
                  Author = "Frank Herbert"
                  Genre = Genre.FICTION
                  Pages = Some 412 }

        let! _ =
            client.AddBookAsync
                { Title = "A Brief History of Time"
                  Author = "Stephen Hawking"
                  Genre = Genre.SCIENCE
                  Pages = None }

        let! lent = client.LendBookAsync { Id = dune.Book.Id; Reader = "Ada" }
        printfn "Lent: %s" (describe lent.Book)

        let! fiction = client.ListBooksAsync { Genre = Some Genre.FICTION }
        printfn "Fiction:"

        for book in fiction.Books do
            printfn "  %s" (describe book)

        // Modeled errors arrive as the exceptions the model declares.
        try
            let! _ = client.LendBookAsync { Id = dune.Book.Id; Reader = "Grace" }
            ()
        with :? AlreadyLent as error ->
            printfn "Cannot lend Dune again: %s has it" error.Reader

        try
            let! _ = client.GetBookAsync { Id = "missing" }
            ()
        with :? NoSuchBook as error ->
            printfn "No book with id %s" error.Id

        // The server validates input against the model before the handler runs.
        try
            let! _ =
                client.AddBookAsync
                    { Title = ""
                      Author = "Nobody"
                      Genre = Genre.HISTORY
                      Pages = Some 0 }

            ()
        with :? ValidationException as error ->
            printfn "Rejected: %s" error.Message
    }

[<EntryPoint>]
let main args =
    let endpoint = args |> Array.tryHead |> Option.defaultValue "http://localhost:5000"
    (run endpoint).Wait()
    0
