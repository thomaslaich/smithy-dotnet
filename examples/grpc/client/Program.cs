using Example.Library;
using Grpc.Net.Client;

var endpoint = args.Length > 0 ? args[0] : "http://localhost:5001";

using var channel = GrpcChannel.ForAddress(endpoint);
var client = new LibraryServiceGrpcClient(channel);

Console.WriteLine("=== GetBook ===");
var getResult = await client.GetBookAsync(new GetBookInput("1"));
var b = getResult.Book;
Console.WriteLine($"  [{b.Format} / {b.Category}] {b.Title} by {b.Author}");
Console.WriteLine($"  Pages: {b.PageCount}  Checksum (fixed64): 0x{b.Checksum:X}");
Console.WriteLine($"  Tags: {string.Join(", ", b.Tags)}");
Console.WriteLine(
    $"  Metadata: {string.Join(", ", b.Metadata.Select(kv => $"{kv.Key}={kv.Value}"))}"
);
Console.WriteLine(
    $"  NullableAttributes (sparse map): {string.Join(", ", b.NullableAttributes.Select(kv => $"{kv.Key}={kv.Value ?? "<null>"}"))}"
);
Console.WriteLine($"  Published: {b.PublishedAt:yyyy-MM-dd}");
Console.WriteLine();

Console.WriteLine("=== CreateBook ===");
var newBook = new Book(
    Id: "",
    Title: "Domain-Driven Design",
    Author: "Eric Evans",
    PageCount: 560,
    Checksum: 0xABCDEF1234567890L,
    Format: BookFormat.HARDCOVER,
    Category: BookCategory.SCIENCE,
    Tags: ["ddd", "architecture", "software design"],
    Metadata: new Dictionary<string, string> { ["publisher"] = "Addison-Wesley" },
    NullableAttributes: new Dictionary<string, string?>
    {
        ["subtitle"] = "Tackling Complexity in the Heart of Software",
        ["series"] = null,
    },
    PublishedAt: new DateTime(2003, 8, 30, 0, 0, 0, DateTimeKind.Utc)
);
var createResult = await client.CreateBookAsync(new CreateBookInput(newBook));
Console.WriteLine($"  Created book with ID: {createResult.Id}");
Console.WriteLine();

Console.WriteLine("=== ListBooks (@protoInlinedOneOf filter — by category) ===");
var listResult = await client.ListBooksAsync(
    new ListBooksInput(PageSize: 10, Filter: new BookFilter.ByCategoryCase(BookCategory.SCIENCE))
);
foreach (var book in listResult.Books)
    Console.WriteLine($"  {book.Title} by {book.Author}");
Console.WriteLine();

Console.WriteLine("=== SearchBooks ===");
var searchResult = await client.SearchBooksAsync(
    new SearchBooksInput(Query: "data", MaxResults: 5)
);
foreach (var book in searchResult.Books)
    Console.WriteLine($"  Match: {book.Title} by {book.Author}");
Console.WriteLine();

Console.WriteLine("=== UploadBooks (batch) ===");
var uploadResult = await client.UploadBooksAsync(
    new UploadBooksInput([
        new Book(
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
            new DateTime(1996, 7, 25, 0, 0, 0, DateTimeKind.Utc)
        ),
        new Book(
            "",
            "The Pragmatic Programmer",
            "David Thomas & Andrew Hunt",
            352,
            0xA9A7169A71CA9A71L,
            BookFormat.PAPERBACK,
            BookCategory.SCIENCE,
            ["programming", "software engineering"],
            new Dictionary<string, string> { ["edition"] = "20th anniversary" },
            new Dictionary<string, string?>(),
            new DateTime(2019, 9, 13, 0, 0, 0, DateTimeKind.Utc)
        ),
    ])
);
Console.WriteLine($"  Uploaded {uploadResult.UploadedCount} books");

Console.WriteLine();
Console.WriteLine("=== DeleteBook ===");
await client.DeleteBookAsync(new DeleteBookInput("1"));
Console.WriteLine("  Book 1 deleted");
