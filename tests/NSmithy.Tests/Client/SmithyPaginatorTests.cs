using NSmithy.Client;

namespace NSmithy.Tests.Client;

public sealed class SmithyPaginatorTests
{
    private sealed record Page(string? Next, int Number);

    [Fact]
    public async Task CallsAgainWithEachTokenUntilAPageHasNone()
    {
        var tokens = new List<string?>();
        var pages = SmithyPaginator.PagesAsync<string?, Page>(
            (token, _) =>
            {
                tokens.Add(token);
                return Task.FromResult(
                    token switch
                    {
                        null => new Page("a", 1),
                        "a" => new Page("b", 2),
                        _ => new Page(null, 3),
                    }
                );
            },
            null,
            static page => page.Next,
            static (_, token) => token
        );

        var numbers = await pages.Select(static page => page.Number).ToListAsync();

        Assert.Equal([1, 2, 3], numbers);
        Assert.Equal([null, "a", "b"], tokens);
    }

    [Fact]
    public async Task StoppingTheEnumerationSendsNoFurtherCalls()
    {
        var calls = 0;
        var pages = SmithyPaginator.PagesAsync<int, Page>(
            (_, _) =>
            {
                calls++;
                return Task.FromResult(new Page("more", calls));
            },
            0,
            static page => page.Next,
            static (input, _) => input
        );

        Assert.Equal(0, calls);

        var first = await pages.FirstAsync();

        Assert.Equal(1, first.Number);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task TheEnumeratorsTokenReachesTheCall()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var pages = SmithyPaginator.PagesAsync<int, Page>(
            static (_, cancellationToken) => Task.FromCanceled<Page>(cancellationToken),
            0,
            static page => page.Next,
            static (input, _) => input
        );

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await pages.FirstAsync(cancelled.Token)
        );
    }
}
