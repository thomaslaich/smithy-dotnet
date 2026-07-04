namespace NSmithy.Core;

/// <summary>
/// Implemented by generated error types that carry the <c>smithy.api#retryable</c> trait.
/// Retry strategies use this to honor modeled retryability regardless of protocol or status code.
/// </summary>
public interface ISmithyRetryableError
{
    /// <summary>True when the error models throttling (<c>@retryable(throttling: true)</c>).</summary>
    bool IsThrottlingError { get; }
}
