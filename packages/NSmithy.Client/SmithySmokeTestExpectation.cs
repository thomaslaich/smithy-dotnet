namespace NSmithy.Client;

/// <summary>What a smoke test expects of its call: success, or an error the service returns.</summary>
public sealed class SmithySmokeTestExpectation
{
    private readonly Func<Exception, bool>? matches;

    private SmithySmokeTestExpectation(string? errorId, Func<Exception, bool>? matches)
    {
        ErrorId = errorId;
        this.matches = matches;
    }

    /// <summary>The call is expected to return.</summary>
    public static SmithySmokeTestExpectation Success { get; } = new(null, null);

    /// <summary>The call is expected to throw an exception <paramref name="matches"/> accepts.</summary>
    /// <param name="errorId">The expected error's shape id, or null for any error from the service.</param>
    /// <param name="matches">Whether a thrown exception is the expected error.</param>
    public static SmithySmokeTestExpectation Failure(string? errorId, Func<Exception, bool> matches)
    {
        ArgumentNullException.ThrowIfNull(matches);
        return new(errorId, matches);
    }

    public bool IsSuccess => matches is null;

    /// <summary>The expected error's shape id. Null on success, or when any error is expected.</summary>
    public string? ErrorId { get; }

    /// <summary>Whether <paramref name="exception"/> is the expected error.</summary>
    public bool Matches(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return matches is not null && matches(exception);
    }
}
