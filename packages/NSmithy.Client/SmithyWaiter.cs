namespace NSmithy.Client;

/// <summary>
/// Polls an operation until one of a waiter's acceptors moves it to a terminal state, following
/// the Smithy waiters specification.
/// </summary>
public static class SmithyWaiter
{
    /// <summary>
    /// Calls <paramref name="call"/> until an acceptor reaches <see cref="SmithyWaiterState.Success"/>
    /// or <see cref="SmithyWaiterState.Failure"/>, or the maximum wait time runs out.
    /// </summary>
    /// <param name="name">The waiter's modeled name, for exceptions.</param>
    /// <param name="call">One attempt: the operation call.</param>
    /// <param name="input">The operation input, the same for every attempt.</param>
    /// <param name="acceptors">The waiter's acceptors, tested in order after each attempt.</param>
    /// <param name="minDelay">The modeled shortest delay between attempts.</param>
    /// <param name="maxDelay">The modeled longest delay between attempts.</param>
    /// <param name="options">The maximum wait time and overrides of the modeled delays.</param>
    /// <param name="cancellationToken">Cancels the current attempt or delay.</param>
    public static async Task<SmithyWaiterResult<TOutput>> WaitAsync<TInput, TOutput>(
        string name,
        Func<TInput, CancellationToken, Task<TOutput>> call,
        TInput input,
        IReadOnlyList<SmithyWaiterAcceptor<TInput, TOutput>> acceptors,
        TimeSpan minDelay,
        TimeSpan maxDelay,
        SmithyWaiterOptions options,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(call);
        ArgumentNullException.ThrowIfNull(acceptors);
        ArgumentNullException.ThrowIfNull(options);

        var min = options.MinDelay ?? minDelay;
        var max = options.MaxDelay ?? maxDelay;
        if (min <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                min,
                "The minimum delay must be positive."
            );
        }
        if (max < min)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                max,
                "The maximum delay must not be less than the minimum delay."
            );
        }
        if (options.MaxWaitTime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.MaxWaitTime,
                "The maximum wait time must be positive."
            );
        }

        var time = options.TimeProvider;
        var start = time.GetTimestamp();
        for (var attempt = 1; ; attempt++)
        {
            TOutput? output = default;
            Exception? exception = null;
            try
            {
                output = await call(input, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception caught)
            {
                exception = caught;
            }

            var state = Match(acceptors, input, output, exception);
            switch (state)
            {
                case SmithyWaiterState.Success:
                    return new SmithyWaiterResult<TOutput>(output, exception, attempt);
                case SmithyWaiterState.Failure:
                    throw new SmithyWaiterFailureException(
                        name,
                        attempt,
                        output,
                        exception,
                        unmatched: false
                    );
                case null when exception is not null:
                    throw new SmithyWaiterFailureException(
                        name,
                        attempt,
                        output,
                        exception,
                        unmatched: true
                    );
            }

            var remaining = options.MaxWaitTime - time.GetElapsedTime(start);
            if (remaining <= min)
            {
                throw new SmithyWaiterTimeoutException(
                    name,
                    attempt,
                    output,
                    exception,
                    options.MaxWaitTime
                );
            }

            await Task.Delay(Delay(attempt, min, max, remaining), time, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The state of the first acceptor that matches, or null when none does.
    /// </summary>
    private static SmithyWaiterState? Match<TInput, TOutput>(
        IReadOnlyList<SmithyWaiterAcceptor<TInput, TOutput>> acceptors,
        TInput input,
        TOutput? output,
        Exception? exception
    )
    {
        foreach (var acceptor in acceptors)
        {
            if (acceptor.Matches(input, output, exception))
            {
                return acceptor.State;
            }
        }
        return null;
    }

    /// <summary>
    /// The delay before attempt <paramref name="attempt"/> + 1: exponential from the minimum
    /// delay, capped at the maximum, with full jitter down to the minimum, and never past the
    /// point where less than the minimum delay would remain.
    /// </summary>
    private static TimeSpan Delay(
        int attempt,
        TimeSpan minDelay,
        TimeSpan maxDelay,
        TimeSpan remaining
    )
    {
        var min = minDelay.TotalSeconds;
        var max = maxDelay.TotalSeconds;
        var ceiling = Math.Log(max / min) / Math.Log(2) + 1;
        var cap = attempt > ceiling ? max : min * Math.Pow(2, attempt - 1);
        var delay = min + Random.Shared.NextDouble() * (cap - min);
        if (remaining.TotalSeconds - delay <= min)
        {
            delay = remaining.TotalSeconds - min;
        }
        return TimeSpan.FromSeconds(delay);
    }
}
