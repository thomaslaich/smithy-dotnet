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
builder.Services.AddGrpc();
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
            Checksum: 0xF05CA1A5CA1A5CA1L,
            Format: BookFormat.PAPERBACK,
            Category: BookCategory.SCIENCE,
            Tags: ["functional programming", "scala", "fp"],
            Metadata: new Dictionary<string, string>
            {
                ["edition"] = "2nd",
                ["publisher"] = "Manning",
            },
            NullableAttributes: new Dictionary<string, string?>
            {
                ["subtitle"] = null,
                ["series"] = "Manning",
            },
            PublishedAt: new DateTime(2023, 8, 29, 0, 0, 0, DateTimeKind.Utc)
        ),
        ["2"] = new Book(
            Id: "2",
            Title: "Designing Data-Intensive Applications",
            Author: "Martin Kleppmann",
            PageCount: 611,
            Checksum: 0xDA7A1A7E51BEDA7AL,
            Format: BookFormat.PAPERBACK,
            Category: BookCategory.SCIENCE,
            Tags: ["distributed systems", "databases", "data engineering"],
            Metadata: new Dictionary<string, string> { ["publisher"] = "O'Reilly" },
            NullableAttributes: new Dictionary<string, string?>
            {
                ["subtitle"] = "The Big Ideas Behind Reliable, Scalable, and Maintainable Systems",
                ["series"] = null,
            },
            PublishedAt: new DateTime(2017, 3, 16, 0, 0, 0, DateTimeKind.Utc)
        ),
        ["3"] = new Book(
            Id: "3",
            Title: "Design Patterns",
            Author: "Erich Gamma, Richard Helm, Ralph Johnson & John Vlissides",
            PageCount: 395,
            Checksum: 0x9A09F04C0DE51B0FL,
            Format: BookFormat.HARDCOVER,
            Category: BookCategory.SCIENCE,
            Tags: ["design patterns", "object-oriented", "gang of four"],
            Metadata: new Dictionary<string, string>
            {
                ["publisher"] = "Addison-Wesley",
                ["series"] = "Addison-Wesley Professional Computing",
            },
            NullableAttributes: new Dictionary<string, string?>
            {
                ["subtitle"] = "Elements of Reusable Object-Oriented Software",
            },
            PublishedAt: new DateTime(1994, 10, 31, 0, 0, 0, DateTimeKind.Utc)
        ),
        ["4"] = new Book(
            Id: "4",
            Title: "C# 13 and .NET 9",
            Author: "Mark J. Price",
            PageCount: 837,
            Checksum: 0xC51300D07E9D07E9L,
            Format: BookFormat.EBOOK,
            Category: BookCategory.SCIENCE,
            Tags: ["csharp", "dotnet", "microsoft"],
            Metadata: new Dictionary<string, string>
            {
                ["publisher"] = "Packt",
                ["edition"] = "9th",
            },
            NullableAttributes: new Dictionary<string, string?>
            {
                ["subtitle"] = "Modern Cross-Platform Development Fundamentals",
                ["series"] = null,
            },
            PublishedAt: new DateTime(2024, 11, 12, 0, 0, 0, DateTimeKind.Utc)
        ),
    };

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
        _books[id] = input.Book with { Id = id };
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
            BookFilter.ByIdCase byId => books.Where(b => b.Id == byId.ById),
            BookFilter.ByTitleCase byTitle => books.Where(b =>
                b.Title.Contains(byTitle.ByTitle, StringComparison.OrdinalIgnoreCase)
            ),
            BookFilter.ByAuthorCase byAuthor => books.Where(b =>
                b.Author.Contains(byAuthor.ByAuthor, StringComparison.OrdinalIgnoreCase)
            ),
            BookFilter.ByCategoryCase byCategory => books.Where(b =>
                b.Category == byCategory.ByCategory
            ),
            _ => books,
        };

        var limit = input.PageSize > 0 ? (int)input.PageSize : int.MaxValue;
        return Task.FromResult(new ListBooksOutput([.. books.Take(limit)]));
    }

    public Task<SearchBooksOutput> SearchBooksAsync(
        SearchBooksInput input,
        CancellationToken cancellationToken = default
    )
    {
        var query = input.Query.ToLowerInvariant();
        var limit = input.MaxResults > 0 ? (int)input.MaxResults : int.MaxValue;

        var matches = _books
            .Values.Where(b =>
                b.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                || b.Author.Contains(query, StringComparison.OrdinalIgnoreCase)
                || b.Tags.Any(t => t.Contains(query, StringComparison.OrdinalIgnoreCase))
            )
            .Take(limit);

        return Task.FromResult(new SearchBooksOutput([.. matches]));
    }

    public Task<UploadBooksOutput> UploadBooksAsync(
        UploadBooksInput input,
        CancellationToken cancellationToken = default
    )
    {
        foreach (var book in input.Books)
        {
            var id = string.IsNullOrEmpty(book.Id) ? Guid.NewGuid().ToString("N")[..8] : book.Id;
            _books[id] = book with { Id = id };
        }
        return Task.FromResult(new UploadBooksOutput((uint)input.Books.Count));
    }
}
