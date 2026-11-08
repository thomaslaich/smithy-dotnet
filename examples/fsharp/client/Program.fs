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

        match! client.LendBookAsync { Id = dune.Book.Id; Reader = "Ada" } with
        | Ok lent -> printfn "Lent: %s" (describe lent.Book)
        | Error error -> printfn "Could not lend Dune: %A" error

        // An input whose members are all optional may be left out.
        let! shelf = client.ListBooksAsync()
        printfn "On the shelf: %d books" shelf.Books.Length

        let! fiction = client.ListBooksAsync { Genre = Some Genre.FICTION }
        printfn "Fiction:"

        for book in fiction.Books do
            printfn "  %s" (describe book)

        // An operation returns the errors the model declares for it as a Result.
        match! client.LendBookAsync { Id = dune.Book.Id; Reader = "Grace" } with
        | Ok lent -> printfn "Lent: %s" (describe lent.Book)
        | Error(LendBookError.AlreadyLent error) -> printfn "Cannot lend Dune again: %s has it" error.Reader
        | Error(LendBookError.NoSuchBook error) -> printfn "No book with id %s" error.Id

        match! client.GetBookAsync { Id = "missing" } with
        | Ok found -> printfn "Found: %s" (describe found.Book)
        | Error(GetBookError.NoSuchBook error) -> printfn "No book with id %s" error.Id

        // Failures the model does not declare stay exceptions. The server validates input against
        // the model before the handler runs, and AddBook does not declare ValidationException.
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
