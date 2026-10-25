namespace NSmithy.Client;

/// <summary>What a waiter does when an acceptor matches.</summary>
public enum SmithyWaiterState
{
    /// <summary>The waiter completes.</summary>
    Success,

    /// <summary>The waiter fails with a <see cref="SmithyWaiterFailureException"/>.</summary>
    Failure,

    /// <summary>The waiter polls again after a delay.</summary>
    Retry,
}

/// <summary>
/// One acceptor of a waiter: the state it moves to, and a test over an attempt's input and its
/// output or exception. Exactly one of the output and the exception is set.
/// </summary>
public sealed record SmithyWaiterAcceptor<TInput, TOutput>(
    SmithyWaiterState State,
    Func<TInput, TOutput?, Exception?, bool> Matches
);
