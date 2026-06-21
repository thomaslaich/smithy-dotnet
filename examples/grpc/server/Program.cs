using Example.Library;
using Microsoft.AspNetCore.Server.Kestrel.Core;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenLocalhost(
        5001,
        listenOptions =>
        {
            listenOptions.Protocols = HttpProtocols.Http2;
        }
    );
});
builder.Services.AddLibraryServiceHandler<LibraryHandler>();

var app = builder.Build();
app.MapLibraryServiceGrpc();
app.Run();

internal sealed class LibraryHandler : ILibraryServiceHandler
{
    // In-memory store for the walking skeleton.
    private static readonly Dictionary<string, Book> _books = new()
    {
        ["1"] = new Book(
            Id: "1",
            Title: "Functional Programming in Scala",
            Author: "Michael Pilquist, Rúnar Bjarnason & Paul Chiusano",
            PageCount: 534,
            Checksum: unchecked((long)0xF05CA1A5CA1A5CA1UL),
            Format: BookFormat.PAPERBACK,
            Category: BookCategory.SCIENCE,
            Tags: new TagList(["functional programming", "scala", "fp"]),
            Metadata: new StringMap(
                new Dictionary<string, string> { ["edition"] = "2nd", ["publisher"] = "Manning" }
            ),
            NullableAttributes: new NullableAttributeMap(
                new Dictionary<string, string?> { ["subtitle"] = null, ["series"] = "Manning" }
            ),
            PublishedAt: new DateTimeOffset(2023, 8, 29, 0, 0, 0, TimeSpan.Zero)
        ),
        ["2"] = new Book(
            Id: "2",
            Title: "Designing Data-Intensive Applications",
            Author: "Martin Kleppmann",
            PageCount: 611,
            Checksum: unchecked((long)0xDA7A1A7E51BEDA7AUL),
            Format: BookFormat.PAPERBACK,
            Category: BookCategory.SCIENCE,
            Tags: new TagList(["distributed systems", "databases", "data engineering"]),
            Metadata: new StringMap(new Dictionary<string, string> { ["publisher"] = "O'Reilly" }),
            NullableAttributes: new NullableAttributeMap(
                new Dictionary<string, string?>
                {
                    ["subtitle"] =
                        "The Big Ideas Behind Reliable, Scalable, and Maintainable Systems",
                    ["series"] = null,
                }
            ),
            PublishedAt: new DateTimeOffset(2017, 3, 16, 0, 0, 0, TimeSpan.Zero)
        ),
        ["3"] = new Book(
            Id: "3",
            Title: "Design Patterns",
            Author: "Erich Gamma, Richard Helm, Ralph Johnson & John Vlissides",
            PageCount: 395,
            Checksum: unchecked((long)0x9A09F04C0DE51B0FUL),
            Format: BookFormat.HARDCOVER,
            Category: BookCategory.SCIENCE,
            Tags: new TagList(["design patterns", "object-oriented", "gang of four"]),
            Metadata: new StringMap(
                new Dictionary<string, string>
                {
                    ["publisher"] = "Addison-Wesley",
                    ["series"] = "Addison-Wesley Professional Computing",
                }
            ),
            NullableAttributes: new NullableAttributeMap(
                new Dictionary<string, string?>
                {
                    ["subtitle"] = "Elements of Reusable Object-Oriented Software",
                }
            ),
            PublishedAt: new DateTimeOffset(1994, 10, 31, 0, 0, 0, TimeSpan.Zero)
        ),
        ["4"] = new Book(
            Id: "4",
            Title: "C# 13 and .NET 9",
            Author: "Mark J. Price",
            PageCount: 837,
            Checksum: unchecked((long)0xC51300D07E9D07E9UL),
            Format: BookFormat.EBOOK,
            Category: BookCategory.SCIENCE,
            Tags: new TagList(["csharp", "dotnet", "microsoft"]),
            Metadata: new StringMap(
                new Dictionary<string, string> { ["publisher"] = "Packt", ["edition"] = "9th" }
            ),
            NullableAttributes: new NullableAttributeMap(
                new Dictionary<string, string?>
                {
                    ["subtitle"] = "Modern Cross-Platform Development Fundamentals",
                    ["series"] = null,
                }
            ),
            PublishedAt: new DateTimeOffset(2024, 11, 12, 0, 0, 0, TimeSpan.Zero)
        ),
    };

    private static Book WithId(Book book, string id)
    {
        return new Book(
            Author: book.Author,
            Id: id,
            Title: book.Title,
            Category: book.Category,
            Checksum: book.Checksum,
            Format: book.Format,
            Metadata: book.Metadata,
            NullableAttributes: book.NullableAttributes,
            PageCount: book.PageCount,
            PublishedAt: book.PublishedAt,
            Tags: book.Tags
        );
    }

    public Task<GetBookOutput> GetBookAsync(
        GetBookInput input,
        CancellationToken cancellationToken = default
    )
    {
        if (!_books.TryGetValue(input.Id, out var book))
            throw new KeyNotFoundException($"Book '{input.Id}' not found.");

        return Task.FromResult(new GetBookOutput(book));
    }

    public Task<CreateBookOutput> CreateBookAsync(
        CreateBookInput input,
        CancellationToken cancellationToken = default
    )
    {
        var id = Guid.NewGuid().ToString("N")[..8];
        _books[id] = WithId(input.Book, id);
        return Task.FromResult(new CreateBookOutput(id));
    }

    public Task<DeleteBookOutput> DeleteBookAsync(
        DeleteBookInput input,
        CancellationToken cancellationToken = default
    )
    {
        _books.Remove(input.Id);
        return Task.FromResult(new DeleteBookOutput());
    }

    public Task<ListBooksOutput> ListBooksAsync(
        ListBooksInput input,
        CancellationToken cancellationToken = default
    )
    {
        var books = _books.Values.AsEnumerable();

        // Apply @protoInlinedOneOf BookFilter (inlined as oneof in the proto)
        books = input.Filter switch
        {
            BookFilter.ById byId => books.Where(b => b.Id == byId.Value),
            BookFilter.ByTitle byTitle => books.Where(b =>
                b.Title.Contains(byTitle.Value, StringComparison.OrdinalIgnoreCase)
            ),
            BookFilter.ByAuthor byAuthor => books.Where(b =>
                b.Author.Contains(byAuthor.Value, StringComparison.OrdinalIgnoreCase)
            ),
            BookFilter.ByCategory byCategory => books.Where(b => b.Category == byCategory.Value),
            _ => books,
        };

        var pageSize = input.PageSize.GetValueOrDefault();
        var limit = pageSize > 0 ? pageSize : int.MaxValue;
        return Task.FromResult(new ListBooksOutput(new BookList(books.Take(limit))));
    }

    public Task<SearchBooksOutput> SearchBooksAsync(
        SearchBooksInput input,
        CancellationToken cancellationToken = default
    )
    {
        var query = input.Query.ToLowerInvariant();
        var maxResults = input.MaxResults.GetValueOrDefault();
        var limit = maxResults > 0 ? maxResults : int.MaxValue;

        var matches = _books
            .Values.Where(b =>
                b.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                || b.Author.Contains(query, StringComparison.OrdinalIgnoreCase)
                || (
                    b.Tags?.Values.Any(t => t.Contains(query, StringComparison.OrdinalIgnoreCase))
                    ?? false
                )
            )
            .Take(limit);

        return Task.FromResult(new SearchBooksOutput(new BookList(matches)));
    }

    public Task<UploadBooksOutput> UploadBooksAsync(
        UploadBooksInput input,
        CancellationToken cancellationToken = default
    )
    {
        foreach (var book in input.Books.Values)
        {
            var id = string.IsNullOrEmpty(book.Id) ? Guid.NewGuid().ToString("N")[..8] : book.Id;
            _books[id] = WithId(book, id);
        }
        return Task.FromResult(new UploadBooksOutput(input.Books.Values.Count));
    }
}
