$version: "2"

namespace example.library

use alloy.proto#grpc
use alloy.proto#protoIndex
use alloy.proto#protoNumType
use alloy.proto#protoInlinedOneOf

/// A library management service that demonstrates NSmithy proto-codegen features:
/// @protoNumType (uint32, fixed64), @sparse maps, @protoInlinedOneOf, intEnum,
/// and string enum generation.
@grpc
service LibraryService {
    version: "2026-05-28"
    operations: [
        GetBook
        CreateBook
        DeleteBook
        ListBooks
        SearchBooks
        UploadBooks
    ]
}

// ─── Operations ──────────────────────────────────────────────────────────────

/// Returns a single book by its ID.
operation GetBook {
    input := {
        @required
        @protoIndex(1)
        id: String
    }
    output := {
        @required
        @protoIndex(1)
        book: Book
    }
}

/// Creates a new book and returns its assigned ID.
operation CreateBook {
    input := {
        @required
        @protoIndex(1)
        book: Book
    }
    output := {
        @required
        @protoIndex(1)
        id: String
    }
}

/// Deletes a book by its ID.
operation DeleteBook {
    input := {
        @required
        @protoIndex(1)
        id: String
    }
    output := {}
}

/// Returns books matching the given filter.
///
/// The `filter` member targets a @protoInlinedOneOf union, so the generated
/// proto inlines its oneof block directly into ListBooksInput:
///
///   message ListBooksInput {
///     optional uint32 page_size = 1;
///     oneof filter {
///       string by_id     = 3;
///       string by_title  = 4;
///       string by_author = 5;
///       BookCategory by_category = 6;
///     }
///   }
operation ListBooks {
    input: ListBooksInput
    output := {
        @required
        @protoIndex(1)
        books: BookList
    }
}

/// Full-text search — returns books whose title, author, or tags match the query.
operation SearchBooks {
    input := {
        @required
        @protoIndex(1)
        query: String

        /// Maximum number of results to return. uint32 via @protoNumType.
        @protoIndex(2)
        @protoNumType("UNSIGNED")
        maxResults: Integer
    }
    output := {
        @required
        @protoIndex(1)
        books: BookList
    }
}

/// Batch-upload books, returns the count of successfully stored books.
operation UploadBooks {
    input := {
        @required
        @protoIndex(1)
        books: BookList
    }
    output := {
        /// Number of books stored. uint32 via @protoNumType.
        @protoIndex(1)
        @protoNumType("UNSIGNED")
        uploadedCount: Integer
    }
}

// ─── Shared input shapes ──────────────────────────────────────────────────────

/// Input for ListBooks. The @protoInlinedOneOf `filter` is inlined into this
/// message in the proto output — field index 2 is reserved by the container
/// and is not emitted as a standalone field.
structure ListBooksInput {
    /// Maximum number of books to return. uint32 via @protoNumType.
    @protoIndex(1)
    @protoNumType("UNSIGNED")
    pageSize: Integer

    /// Filter selector. @protoInlinedOneOf: the oneof replaces this field.
    @protoIndex(2)
    filter: BookFilter
}

// ─── Core domain shapes ───────────────────────────────────────────────────────

/// A book in the library.
///
/// Demonstrates several proto features:
///   - @protoNumType("UNSIGNED") → uint32  for pageCount (always non-negative)
///   - @protoNumType("FIXED")    → fixed64 for checksum  (constant-width hash)
///   - @sparse map               → google.protobuf.Value for nullable attributes
///   - Timestamp                 → google.protobuf.Timestamp
structure Book {
    @required
    @protoIndex(1)
    id: String

    @required
    @protoIndex(2)
    title: String

    @required
    @protoIndex(3)
    author: String

    /// Page count is always ≥ 0 — uint32 via @protoNumType("UNSIGNED").
    @protoIndex(4)
    @protoNumType("UNSIGNED")
    pageCount: Integer

    /// CRC/hash value — fixed64 via @protoNumType("FIXED") for
    /// constant serialisation width regardless of the numeric value.
    @protoIndex(5)
    @protoNumType("FIXED")
    checksum: Long

    @protoIndex(6)
    format: BookFormat

    @protoIndex(7)
    category: BookCategory

    /// Free-form tags (repeated string).
    @protoIndex(8)
    tags: TagList

    /// Arbitrary key/value metadata (map<string, string>).
    @protoIndex(9)
    metadata: StringMap

    /// Optional attributes where individual values may be null.
    /// @sparse makes the proto value type google.protobuf.Value,
    /// allowing null values in the map.
    @protoIndex(10)
    nullableAttributes: NullableAttributeMap

    @protoIndex(11)
    publishedAt: Timestamp
}

/// @protoInlinedOneOf — instead of generating a wrapper message, the union's
/// oneof block is inlined directly into the parent message (ListBooksInput).
/// Indices 3-6 must not collide with ListBooksInput's other field indices (1, 2).
@protoInlinedOneOf
union BookFilter {
    @protoIndex(3)
    byId: String

    @protoIndex(4)
    byTitle: String

    @protoIndex(5)
    byAuthor: String

    @protoIndex(6)
    byCategory: BookCategory
}

// ─── Enums ────────────────────────────────────────────────────────────────────

/// Physical/digital format of a book.
enum BookFormat {
    PAPERBACK
    HARDCOVER
    EBOOK
    AUDIOBOOK
}

/// Subject category using integer codes.
/// Proto3 requires a zero value; UNSPECIFIED = 0 serves as the default/unknown case.
intEnum BookCategory {
    UNSPECIFIED = 0
    FICTION = 1
    NON_FICTION = 2
    SCIENCE = 3
    HISTORY = 4
    BIOGRAPHY = 5
}

// ─── Collection shapes ────────────────────────────────────────────────────────

list BookList {
    member: Book
}

list TagList {
    member: String
}

map StringMap {
    key: String
    value: String
}

/// @sparse map — null values are permitted.
/// The proto value type becomes google.protobuf.Value instead of string,
/// and struct.proto is imported automatically.
@sparse
map NullableAttributeMap {
    key: String
    value: String
}
