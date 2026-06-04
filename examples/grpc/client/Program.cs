using Example.Library;
using Grpc.Net.Client;

var endpoint = args.Length > 0 ? args[0] : "http://localhost:5001";

using var channel = GrpcChannel.ForAddress(endpoint);
var client = new LibraryServiceGrpcClient(channel);

static Book NewBook(
    string id,
    string title,
    string author,
    int pageCount,
    long checksum,
    BookFormat format,
    BookCategory category,
    IEnumerable<string> tags,
    IReadOnlyDictionary<string, string> metadata,
    IReadOnlyDictionary<string, string?> nullableAttributes,
    DateTimeOffset publishedAt
)
{
    return new Book(
        id: id,
        title: title,
        author: author,
        pageCount: pageCount,
        checksum: checksum,
        format: format,
        category: category,
        tags: new TagList(tags),
        metadata: new StringMap(metadata),
        nullableAttributes: new NullableAttributeMap(nullableAttributes),
        publishedAt: publishedAt
    );
}

Console.WriteLine("=== GetBook ===");
var getResult = await client.GetBookAsync(new GetBookInput("1"));
var b = getResult.Book;
Console.WriteLine($"  [{b.Format} / {b.Category}] {b.Title} by {b.Author}");
Console.WriteLine($"  Pages: {b.PageCount}  Checksum (fixed64): 0x{b.Checksum:X}");
Console.WriteLine($"  Tags: {string.Join(", ", b.Tags?.Values ?? [])}");
Console.WriteLine(
    $"  Metadata: {string.Join(", ", (b.Metadata?.Values ?? new Dictionary<string, string>()).Select(kv => $"{kv.Key}={kv.Value}"))}"
);
Console.WriteLine(
    $"  NullableAttributes (sparse map): {string.Join(", ", (b.NullableAttributes?.Values ?? new Dictionary<string, string?>()).Select(kv => $"{kv.Key}={kv.Value ?? "<null>"}"))}"
);
Console.WriteLine($"  Published: {b.PublishedAt:yyyy-MM-dd}");
Console.WriteLine();

Console.WriteLine("=== CreateBook ===");
var newBook = NewBook(
    id: "",
    title: "Domain-Driven Design",
    author: "Eric Evans",
    pageCount: 560,
    checksum: unchecked((long)0xABCDEF1234567890UL),
    format: BookFormat.HARDCOVER,
    category: BookCategory.SCIENCE,
    tags: ["ddd", "architecture", "software design"],
    metadata: new Dictionary<string, string> { ["publisher"] = "Addison-Wesley" },
    nullableAttributes: new Dictionary<string, string?>
    {
        ["subtitle"] = "Tackling Complexity in the Heart of Software",
        ["series"] = null,
    },
    publishedAt: new DateTimeOffset(2003, 8, 30, 0, 0, 0, TimeSpan.Zero)
);
var createResult = await client.CreateBookAsync(new CreateBookInput(newBook));
Console.WriteLine($"  Created book with ID: {createResult.Id}");
Console.WriteLine();

Console.WriteLine("=== ListBooks (@protoInlinedOneOf filter — by category) ===");
var listResult = await client.ListBooksAsync(
    new ListBooksInput(filter: BookFilter.FromByCategory(BookCategory.SCIENCE), pageSize: 10)
);
foreach (var book in listResult.Books.Values)
    Console.WriteLine($"  {book.Title} by {book.Author}");
Console.WriteLine();

Console.WriteLine("=== SearchBooks ===");
var searchResult = await client.SearchBooksAsync(
    new SearchBooksInput(query: "data", maxResults: 5)
);
foreach (var book in searchResult.Books.Values)
    Console.WriteLine($"  Match: {book.Title} by {book.Author}");
Console.WriteLine();

Console.WriteLine("=== UploadBooks (batch) ===");
var uploadResult = await client.UploadBooksAsync(
    new UploadBooksInput(
        new BookList([
            NewBook(
                "",
                "Structure and Interpretation of Computer Programs",
                "Harold Abelson & Gerald Jay Sussman",
                657,
                0x51CA0ABE10B50101L,
                BookFormat.EBOOK,
                BookCategory.SCIENCE,
                ["sicp", "lisp", "computer science"],
                new Dictionary<string, string> { ["publisher"] = "MIT Press" },
                new Dictionary<string, string?>(),
                new DateTimeOffset(1996, 7, 25, 0, 0, 0, TimeSpan.Zero)
            ),
            NewBook(
                "",
                "The Pragmatic Programmer",
                "David Thomas & Andrew Hunt",
                352,
                unchecked((long)0xA9A7169A71CA9A71UL),
                BookFormat.PAPERBACK,
                BookCategory.SCIENCE,
                ["programming", "software engineering"],
                new Dictionary<string, string> { ["edition"] = "20th anniversary" },
                new Dictionary<string, string?>(),
                new DateTimeOffset(2019, 9, 13, 0, 0, 0, TimeSpan.Zero)
            ),
        ])
    )
);
Console.WriteLine($"  Uploaded {uploadResult.UploadedCount} books");

Console.WriteLine();
Console.WriteLine("=== DeleteBook ===");
await client.DeleteBookAsync(new DeleteBookInput("1"));
Console.WriteLine("  Book 1 deleted");
