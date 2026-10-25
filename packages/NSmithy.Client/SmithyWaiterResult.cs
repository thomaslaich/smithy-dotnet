namespace NSmithy.Client;

/// <summary>
/// A waiter that reached its success state: the output or exception of the attempt a success
/// acceptor matched, and the number of attempts made.
/// </summary>
public sealed record SmithyWaiterResult<TOutput>(
    TOutput? Output,
    Exception? Exception,
    int Attempts
);
