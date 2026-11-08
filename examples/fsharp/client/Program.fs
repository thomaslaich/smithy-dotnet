open System
open Example.Bookshelf

/// The output of a call the example expects to succeed.
let expectOk (result: Result<'output, 'error>) =
    match result with
    | Ok output -> output
    | Error error -> failwith $"Unexpected error: {error}"

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

        // AddBook's input is constrained, so the server may reject it with ValidationException,
        // and the client returns a Result with that case even though the model declares no error.
        let! added =
            client.AddBookAsync
                { Title = "Dune"
                  Author = "Frank Herbert"
                  Genre = Genre.FICTION
                  Pages = Some 412 }

        let dune = expectOk added

        let! _ =
            client.AddBookAsync
                { Title = "A Brief History of Time"
                  Author = "Stephen Hawking"
                  Genre = Genre.SCIENCE
                  Pages = None }

        match! client.LendBookAsync { Id = dune.Book.Id; Reader = "Ada" } with
        | Ok lent -> printfn "Lent: %s" (describe lent.Book)
        | Error error -> printfn "Could not lend Dune: %A" error

        // An input whose members are all optional may be left out. The genre is an enum, which
        // the server checks, so this operation returns a Result too.
        let! shelf = client.ListBooksAsync()
        printfn "On the shelf: %d books" (expectOk shelf).Books.Length

        let! fiction = client.ListBooksAsync { Genre = Some Genre.FICTION }
        printfn "Fiction:"

        for book in (expectOk fiction).Books do
            printfn "  %s" (describe book)

        // An operation returns the errors the model declares for it as a Result.
        match! client.LendBookAsync { Id = dune.Book.Id; Reader = "Grace" } with
        | Ok lent -> printfn "Lent: %s" (describe lent.Book)
        | Error(LendBookError.AlreadyLent error) -> printfn "Cannot lend Dune again: %s has it" error.Reader
        | Error(LendBookError.NoSuchBook error) -> printfn "No book with id %s" error.Id
        | Error(LendBookError.ValidationException error) -> printfn "Rejected: %s" error.Message

        match! client.GetBookAsync { Id = "missing" } with
        | Ok found -> printfn "Found: %s" (describe found.Book)
        | Error(GetBookError.NoSuchBook error) -> printfn "No book with id %s" error.Id
        | Error(GetBookError.ValidationException error) -> printfn "Rejected: %s" error.Message

        // The server validates input against the model before the handler runs. The rejection
        // comes back as the ValidationException case, naming each member and the constraint it
        // failed.
        match!
            client.AddBookAsync
                { Title = ""
                  Author = "Nobody"
                  Genre = Genre.HISTORY
                  Pages = Some 0 }
        with
        | Ok rejected -> printfn "Unexpectedly added: %s" (describe rejected.Book)
        | Error(AddBookError.ValidationException error) ->
            printfn "Rejected: %s" error.Message

            for field in error.FieldList do
                printfn "  %s: %s" field.Path field.Message
    }

[<EntryPoint>]
let main args =
    let endpoint = args |> Array.tryHead |> Option.defaultValue "http://localhost:5000"
    (run endpoint).Wait()
    0
