namespace NSmithy.Aws;

/// <summary>An expected credentials-provider failure with enough context to diagnose the chain.</summary>
public sealed class AwsCredentialsProviderException : Exception
{
    public AwsCredentialsProviderException(
        string providerName,
        string message,
        bool isNotConfigured = false,
        Exception? innerException = null
    )
        : base(message, innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);
        ProviderName = providerName;
        IsNotConfigured = isNotConfigured;
    }

    public string ProviderName { get; }

    /// <summary>
    /// True when the provider has no configuration in this environment, so a default chain may
    /// safely continue to the next source. False means configuration existed but was invalid or
    /// the configured source failed.
    /// </summary>
    public bool IsNotConfigured { get; }
}
