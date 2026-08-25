using NSmithy.Client;

namespace NSmithy.Aws;

/// <summary>
/// Resolves the standard regional AWS endpoint pattern across commercial, China, GovCloud, and
/// isolated partitions. Services with modeled endpoint rules can still replace this resolver.
/// </summary>
public sealed class AwsRegionalEndpointResolver : IEndpointResolver
{
    private readonly SmithyEndpoint endpoint;

    public AwsRegionalEndpointResolver(
        string endpointPrefix,
        string region,
        bool useFips = false,
        bool useDualStack = false,
        IReadOnlyDictionary<string, string>? headers = null,
        IReadOnlyList<string>? authSchemes = null
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpointPrefix);
        ArgumentException.ThrowIfNullOrWhiteSpace(region);
        var partition = AwsPartition.ForRegion(region);
        var hostPrefix = endpointPrefix + (useFips ? "-fips" : string.Empty);
        var dnsSuffix = useDualStack
            ? partition.DualStackDnsSuffix
                ?? throw new NotSupportedException(
                    $"The AWS partition for region '{region}' does not define dual-stack endpoints."
                )
            : partition.DnsSuffix;
        var host = $"{hostPrefix}.{region}.{dnsSuffix}";
        endpoint = new SmithyEndpoint(new Uri("https://" + host), headers, authSchemes);
    }

    public ValueTask<SmithyEndpoint> ResolveEndpointAsync(
        SmithyEndpointParameters parameters,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(endpoint);
    }

    public SmithyEndpoint? StaticEndpoint => endpoint;

    private sealed record AwsPartition(string DnsSuffix, string? DualStackDnsSuffix)
    {
        public static AwsPartition ForRegion(string region)
        {
            if (region.StartsWith("cn-", StringComparison.Ordinal))
            {
                return new AwsPartition("amazonaws.com.cn", "api.amazonwebservices.com.cn");
            }
            if (region.StartsWith("us-iso-", StringComparison.Ordinal))
            {
                return new AwsPartition("c2s.ic.gov", null);
            }
            if (region.StartsWith("us-isob-", StringComparison.Ordinal))
            {
                return new AwsPartition("sc2s.sgov.gov", null);
            }
            if (region.StartsWith("eu-isoe-", StringComparison.Ordinal))
            {
                return new AwsPartition("cloud.adc-e.uk", null);
            }
            if (region.StartsWith("us-isof-", StringComparison.Ordinal))
            {
                return new AwsPartition("csp.hci.ic.gov", null);
            }
            return new AwsPartition("amazonaws.com", "api.aws");
        }
    }
}
