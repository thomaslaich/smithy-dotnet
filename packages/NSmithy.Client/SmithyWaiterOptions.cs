namespace NSmithy.Client;

/// <summary>How long a waiter waits, and the delays between its attempts.</summary>
/// <param name="MaxWaitTime">The longest the waiter polls before it times out.</param>
public sealed record SmithyWaiterOptions(TimeSpan MaxWaitTime)
{
    /// <summary>The shortest delay between attempts. Default: the waiter's modeled delay.</summary>
    public TimeSpan? MinDelay { get; init; }

    /// <summary>The longest delay between attempts. Default: the waiter's modeled delay.</summary>
    public TimeSpan? MaxDelay { get; init; }

    /// <summary>The clock and timers the waiter uses.</summary>
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;
}
