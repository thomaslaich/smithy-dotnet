namespace NSmithy.Client;

/// <summary>A waiter that ended without reaching its success state.</summary>
public abstract class SmithyWaiterException : Exception
{
    private protected SmithyWaiterException(
        string waiterName,
        int attempts,
        object? lastOutput,
        Exception? lastException,
        string message
    )
        : base(message, lastException)
    {
        WaiterName = waiterName;
        Attempts = attempts;
        LastOutput = lastOutput;
    }

    /// <summary>The waiter's modeled name, such as <c>TableExists</c>.</summary>
    public string WaiterName { get; }

    /// <summary>The number of attempts made.</summary>
    public int Attempts { get; }

    /// <summary>The output of the last attempt, when it returned one.</summary>
    public object? LastOutput { get; }

    /// <summary>The exception of the last attempt, when it threw one.</summary>
    public Exception? LastException => InnerException;
}

/// <summary>
/// A waiter whose failure acceptor matched, or whose attempt threw an exception no acceptor
/// matched.
/// </summary>
public sealed class SmithyWaiterFailureException : SmithyWaiterException
{
    internal SmithyWaiterFailureException(
        string waiterName,
        int attempts,
        object? lastOutput,
        Exception? lastException,
        bool unmatched
    )
        : base(
            waiterName,
            attempts,
            lastOutput,
            lastException,
            unmatched
                ? $"Waiter {waiterName} failed after {attempts} attempt(s): the operation threw an error no acceptor matched."
                : $"Waiter {waiterName} reached a failure state after {attempts} attempt(s)."
        ) { }
}

/// <summary>A waiter that did not reach a terminal state within its maximum wait time.</summary>
public sealed class SmithyWaiterTimeoutException : SmithyWaiterException
{
    internal SmithyWaiterTimeoutException(
        string waiterName,
        int attempts,
        object? lastOutput,
        Exception? lastException,
        TimeSpan maxWaitTime
    )
        : base(
            waiterName,
            attempts,
            lastOutput,
            lastException,
            $"Waiter {waiterName} timed out after {attempts} attempt(s) and {maxWaitTime}."
        ) { }
}
