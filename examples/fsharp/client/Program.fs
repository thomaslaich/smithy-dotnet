open System
open Example.Bookshelf

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

        let! added =
            client.AddBookAsync
                { Title = "Dune"
                  Author = "Frank Herbert"
                  Genre = Genre.FICTION
                  Pages = Some 412 }

        let dune =
            match added with
            | Ok added -> added.Book
            | Error(AddBookError.ValidationException error) -> failwith $"Rejected: {error.Message}"

        let! _ =
            client.AddBookAsync
                { Title = "A Brief History of Time"
                  Author = "Stephen Hawking"
                  Genre = Genre.SCIENCE
                  Pages = None }

        match! client.LendBookAsync { Id = dune.Id; Reader = "Ada" } with
        | Ok lent -> printfn "Lent: %s" (describe lent.Book)
        | Error error -> printfn "Could not lend Dune: %A" error

        // An input whose members are all optional may be left out.
        match! client.ListBooksAsync() with
        | Ok shelf -> printfn "On the shelf: %d books" shelf.Books.Length
        | Error(ListBooksError.ValidationException error) -> printfn "Rejected: %s" error.Message

        match! client.ListBooksAsync { Genre = Some Genre.FICTION } with
        | Ok fiction ->
            printfn "Fiction:"

            for book in fiction.Books do
                printfn "  %s" (describe book)
        | Error(ListBooksError.ValidationException error) -> printfn "Rejected: %s" error.Message

        // An operation returns the errors the model declares for it as a Result.
        match! client.LendBookAsync { Id = dune.Id; Reader = "Grace" } with
        | Ok lent -> printfn "Lent: %s" (describe lent.Book)
        | Error(LendBookError.AlreadyLent error) -> printfn "Cannot lend Dune again: %s has it" error.Reader
        | Error(LendBookError.NoSuchBook error) -> printfn "No book with id %s" error.Id
        | Error(LendBookError.ValidationException error) -> printfn "Rejected: %s" error.Message

        match! client.GetBookAsync { Id = "missing" } with
        | Ok found -> printfn "Found: %s" (describe found.Book)
        | Error(GetBookError.NoSuchBook error) -> printfn "No book with id %s" error.Id
        | Error(GetBookError.ValidationException error) -> printfn "Rejected: %s" error.Message

        // The server validates input before the handler runs; the rejection is a case too.
        match!
            client.AddBookAsync
                { Title = ""
                  Author = "Nobody"
                  Genre = Genre.HISTORY
                  Pages = Some 0 }
        with
        | Ok added -> printfn "Added: %s" (describe added.Book)
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
