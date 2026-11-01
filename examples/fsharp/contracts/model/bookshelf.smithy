$version: "2"

namespace example.bookshelf

use aws.protocols#restJson1

/// A shelf of books that readers can add to, look up, and lend.
@restJson1
service Bookshelf {
    version: "2026-11-01"
    operations: [AddBook, GetBook, ListBooks, LendBook]
}

/// Adds a book to the shelf.
@http(method: "POST", uri: "/books", code: 201)
operation AddBook {
    input := {
        @required
        @length(min: 1, max: 200)
        title: String

        @required
        author: String

        @required
        genre: Genre

        /// Pages, when known.
        @range(min: 1)
        pages: Integer
    }
    output := {
        @required
        book: Book
    }
}

/// Looks up one book.
@readonly
@http(method: "GET", uri: "/books/{id}")
operation GetBook {
    input := {
        @required
        @httpLabel
        id: String
    }
    output := {
        @required
        book: Book
    }
    errors: [NoSuchBook]
}

/// Lists the books on the shelf, optionally of one genre.
@readonly
@http(method: "GET", uri: "/books")
operation ListBooks {
    input := {
        @httpQuery("genre")
        genre: Genre
    }
    output := {
        @required
        books: Books
    }
}

/// Lends a book to a reader. A book that is already lent cannot be lent again.
@http(method: "POST", uri: "/books/{id}/loans")
operation LendBook {
    input := {
        @required
        @httpLabel
        id: String

        @required
        reader: String
    }
    output := {
        @required
        book: Book
    }
    errors: [NoSuchBook, AlreadyLent]
}

structure Book {
    @required
    id: String

    @required
    title: String

    @required
    author: String

    @required
    genre: Genre

    pages: Integer

    @required
    status: Status
}

list Books {
    member: Book
}

enum Genre {
    FICTION = "fiction"
    SCIENCE = "science"
    HISTORY = "history"
}

/// Where a book is.
union Status {
    onShelf: Unit

    /// The reader who has it.
    lentTo: String
}

/// No book has this id.
@error("client")
@httpError(404)
structure NoSuchBook {
    @required
    id: String
}

/// The book is already lent.
@error("client")
@httpError(409)
structure AlreadyLent {
    @required
    reader: String
}
