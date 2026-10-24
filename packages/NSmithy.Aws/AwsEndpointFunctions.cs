using System.Text.RegularExpressions;

namespace NSmithy.Aws;

/// <summary>
/// The AWS functions of the Smithy rules engine, called by generated endpoint resolvers.
/// Functions that return an optional value return null for "not set".
/// </summary>
public static class AwsEndpointFunctions
{
    /// <summary>
    /// <c>aws.partition</c>: the partition a region belongs to. A region the partition data lists
    /// matches its partition, any other region the first partition whose region pattern matches it,
    /// and a region nothing matches the <c>aws</c> partition.
    /// </summary>
    public static AwsPartition Partition(string region)
    {
        ArgumentNullException.ThrowIfNull(region);
        return AwsPartitions.Find(region);
    }

    /// <summary>
    /// <c>aws.parseArn</c>: splits an ARN into its parts. Returns null unless the value has the
    /// form <c>arn:partition:service:region:account-id:resource</c> with a non-empty partition,
    /// service, and resource.
    /// </summary>
    public static AwsArn? ParseArn(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (value.Length < 8 || !value.StartsWith("arn:", StringComparison.Ordinal))
        {
            return null;
        }
        var separators = new int[5];
        separators[0] = 3;
        for (var i = 1; i < separators.Length; i++)
        {
            separators[i] = value.IndexOf(':', separators[i - 1] + 1);
            if (separators[i] < 0)
            {
                return null;
            }
        }

        var partition = value[(separators[0] + 1)..separators[1]];
        var service = value[(separators[1] + 1)..separators[2]];
        var region = value[(separators[2] + 1)..separators[3]];
        var accountId = value[(separators[3] + 1)..separators[4]];
        var resource = value[(separators[4] + 1)..];
        if (partition.Length == 0 || service.Length == 0 || resource.Length == 0)
        {
            return null;
        }
        return new AwsArn(partition, service, region, accountId, resource.Split(':', '/'));
    }

    /// <summary>
    /// <c>aws.isVirtualHostableS3Bucket</c>: whether a bucket name can be used as a host label
    /// (3 to 63 lowercase letters, digits, and hyphens, starting and ending with a letter or
    /// digit). With <paramref name="allowSubDomains"/>, dots may separate such labels, as long
    /// as no dot is next to a dot or hyphen and the name is not an IPv4 address.
    /// </summary>
    public static bool IsVirtualHostableS3Bucket(string value, bool allowSubDomains)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (
            value.Length is < 3 or > 63
            || !IsLowercaseLetterOrDigit(value[0])
            || !IsLowercaseLetterOrDigit(value[^1])
        )
        {
            return false;
        }
        if (!allowSubDomains)
        {
            foreach (var c in value.AsSpan(1, value.Length - 2))
            {
                if (!IsLowercaseLetterOrDigit(c) && c != '-')
                {
                    return false;
                }
            }
            return true;
        }

        var previous = value[0];
        foreach (var c in value.AsSpan(1))
        {
            var valid = c switch
            {
                '.' => previous is not ('.' or '-'),
                '-' => previous != '.',
                _ => IsLowercaseLetterOrDigit(c),
            };
            if (!valid)
            {
                return false;
            }
            previous = c;
        }
        return !IsIpv4Address(value);
    }

    private static bool IsLowercaseLetterOrDigit(char c) =>
        char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c);

    // The rules engine's heuristic: four dot-separated groups of one to three digits without
    // leading zeros.
    private static bool IsIpv4Address(string value)
    {
        var groups = value.Split('.');
        return groups.Length == 4
            && groups.All(group =>
                group.Length is >= 1 and <= 3
                && (group.Length == 1 || group[0] != '0')
                && group.All(char.IsAsciiDigit)
            );
    }
}

/// <summary>The partition returned by the rules engine's <c>aws.partition</c>.</summary>
/// <param name="Name">The partition name, such as <c>aws</c> or <c>aws-cn</c>.</param>
/// <param name="DnsSuffix">The DNS suffix of the partition's endpoints.</param>
/// <param name="DualStackDnsSuffix">The DNS suffix of its dual-stack endpoints.</param>
/// <param name="SupportsFips">Whether the partition has FIPS endpoints.</param>
/// <param name="SupportsDualStack">Whether the partition has dual-stack endpoints.</param>
/// <param name="ImplicitGlobalRegion">The region global endpoints of the partition sign for.</param>
public sealed record AwsPartition(
    string Name,
    string DnsSuffix,
    string DualStackDnsSuffix,
    bool SupportsFips,
    bool SupportsDualStack,
    string ImplicitGlobalRegion
);

/// <summary>The parts of an ARN parsed by the rules engine's <c>aws.parseArn</c>.</summary>
/// <param name="Partition">The partition, such as <c>aws</c>.</param>
/// <param name="Service">The service namespace, such as <c>s3</c>.</param>
/// <param name="Region">The region, possibly empty.</param>
/// <param name="AccountId">The account id, possibly empty.</param>
/// <param name="ResourceId">The resource, split on <c>:</c> and <c>/</c>.</param>
public sealed record AwsArn(
    string Partition,
    string Service,
    string Region,
    string AccountId,
    IReadOnlyList<string> ResourceId
);

internal sealed record AwsPartitionDefinition(
    string Id,
    string RegionRegex,
    string[] Regions,
    AwsPartition Outputs
)
{
    // ECMAScript semantics keep \w and \d ASCII-only, as in the Java patterns they come from.
    public Regex RegionPattern { get; } =
        new(RegionRegex, RegexOptions.ECMAScript | RegexOptions.CultureInvariant);
}

internal static partial class AwsPartitions
{
    public static AwsPartition Find(string region)
    {
        if (Index.ByRegion.TryGetValue(region, out var partition))
        {
            return partition;
        }
        foreach (var definition in All)
        {
            if (definition.RegionPattern.IsMatch(region))
            {
                return definition.Outputs;
            }
        }
        return Index.Aws;
    }

    // A nested class, so its initializers run after All's, which another part of the class declares.
    private static class Index
    {
        public static readonly Dictionary<string, AwsPartition> ByRegion = All.SelectMany(
                partition => partition.Regions,
                (partition, region) => KeyValuePair.Create(region, partition.Outputs)
            )
            .ToDictionary(StringComparer.Ordinal);

        public static readonly AwsPartition Aws = All.Single(p => p.Id == "aws").Outputs;
    }
}
