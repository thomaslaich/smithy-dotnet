namespace NSmithy.Client;

/// <summary>
/// The outcome of a smoke test: whether it passed, why not, and what the call threw, if anything.
/// </summary>
public sealed record SmithySmokeTestResult(
    string Id,
    bool Passed,
    string? Failure,
    Exception? Exception
)
{
    internal static SmithySmokeTestResult Pass(string id, Exception? exception) =>
        new(id, true, null, exception);

    internal static SmithySmokeTestResult Fail(string id, string failure, Exception? exception) =>
        new(id, false, failure, exception);
}
