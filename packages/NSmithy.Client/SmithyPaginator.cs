using System.Runtime.CompilerServices;

namespace NSmithy.Client;

/// <summary>
/// Pages through an operation modeled with <c>@paginated</c>, for generated code that cannot
/// write an async iterator itself.
/// </summary>
public static class SmithyPaginator
{
    /// <summary>
    /// Calls <paramref name="call"/> with <paramref name="input"/>, then again with each page's
    /// continuation token, until a page has none. Stopping the enumeration sends no further
    /// requests.
    /// </summary>
    /// <param name="call">One page: the operation call.</param>
    /// <param name="input">The input of the first call.</param>
    /// <param name="nextToken">The page's continuation token, or <see langword="null"/> after the last page.</param>
    /// <param name="withToken">The input with its continuation token set.</param>
    /// <param name="cancellationToken">Cancels the current call.</param>
    public static IAsyncEnumerable<TPage> PagesAsync<TInput, TPage>(
        Func<TInput, CancellationToken, Task<TPage>> call,
        TInput input,
        Func<TPage, string?> nextToken,
        Func<TInput, string, TInput> withToken,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(call);
        ArgumentNullException.ThrowIfNull(nextToken);
        ArgumentNullException.ThrowIfNull(withToken);
        return Pages(call, input, nextToken, withToken, cancellationToken);
    }

    private static async IAsyncEnumerable<TPage> Pages<TInput, TPage>(
        Func<TInput, CancellationToken, Task<TPage>> call,
        TInput input,
        Func<TPage, string?> nextToken,
        Func<TInput, string, TInput> withToken,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        var page = await call(input, cancellationToken).ConfigureAwait(false);
        yield return page;
        var token = nextToken(page);
        while (token is not null)
        {
            input = withToken(input, token);
            page = await call(input, cancellationToken).ConfigureAwait(false);
            yield return page;
            token = nextToken(page);
        }
    }
}
