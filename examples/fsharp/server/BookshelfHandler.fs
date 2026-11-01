module Bookshelf.Server.BookshelfHandler

open System
open System.Collections.Concurrent
open System.Threading.Tasks
open Example.Bookshelf

/// Keeps the shelf in memory. The runtime has already validated each input against the model,
/// so a handler only deals with the business rules.
type BookshelfHandler() =
    let books = ConcurrentDictionary<string, Book>()

    let find id =
        match books.TryGetValue id with
        | true, book -> book
        | false, _ -> raise (NoSuchBook(id))

    interface IBookshelfServiceHandler with
        member _.AddBookAsync(input, _) =
            let book =
                { Id = Guid.NewGuid().ToString("N").[..7]
                  Title = input.Title
                  Author = input.Author
                  Genre = input.Genre
                  Pages = input.Pages
                  Status = Status.OnShelf }

            books[book.Id] <- book
            Task.FromResult { Book = book }

        member _.GetBookAsync(input, _) =
            Task.FromResult { Book = find input.Id }

        member _.ListBooksAsync(input, _) =
            let shelf =
                books.Values
                |> Seq.filter (fun book -> input.Genre |> Option.forall ((=) book.Genre))
                |> Seq.sortBy _.Title
                |> List.ofSeq

            Task.FromResult { Books = shelf }

        member _.LendBookAsync(input, _) =
            let book = find input.Id

            match book.Status with
            | Status.LentTo reader -> raise (AlreadyLent(reader))
            | _ ->
                let lent =
                    { book with
                        Status = Status.LentTo input.Reader }

                books[lent.Id] <- lent
                Task.FromResult { Book = lent }
