module Bookshelf.Server.BookshelfHandler

open System
open System.Collections.Concurrent
open System.Threading.Tasks
open Example.Bookshelf

/// Keeps the shelf in memory. The runtime has already validated each input against the model,
/// so a handler only deals with the business rules.
let create () =
    let books = ConcurrentDictionary<string, Book>()

    let find id =
        match books.TryGetValue id with
        | true, book -> Ok book
        | false, _ -> Error({ Id = id }: NoSuchBook)

    { new IBookshelfServiceHandler with
        member _.AddBookAsync(input, _) =
            let book =
                { Id = Guid.NewGuid().ToString("N").[..7]
                  Title = input.Title
                  Author = input.Author
                  Genre = input.Genre
                  Pages = input.Pages
                  Status = Status.OnShelf }

            books[book.Id] <- book
            Task.FromResult(Ok { Book = book })

        member _.GetBookAsync(input, _) =
            find input.Id
            |> Result.map (fun book -> ({ Book = book }: GetBookOutput))
            |> Result.mapError GetBookError.NoSuchBook
            |> Task.FromResult

        member _.ListBooksAsync(input, _) =
            let shelf =
                books.Values
                |> Seq.filter (fun book -> input.Genre |> Option.forall ((=) book.Genre))
                |> Seq.sortBy _.Title
                |> List.ofSeq

            Task.FromResult(Ok { Books = shelf })

        member _.LendBookAsync(input, _) =
            let lend book =
                match book.Status with
                | Status.LentTo reader -> Error(LendBookError.AlreadyLent { Reader = reader })
                | _ ->
                    let lent =
                        { book with
                            Status = Status.LentTo input.Reader }

                    books[lent.Id] <- lent
                    Ok { Book = lent }

            find input.Id
            |> Result.mapError LendBookError.NoSuchBook
            |> Result.bind lend
            |> Task.FromResult }
