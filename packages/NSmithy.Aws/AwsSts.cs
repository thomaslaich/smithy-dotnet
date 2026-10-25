using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using NSmithy.Client;
using NSmithy.Http;

namespace NSmithy.Aws;

/// <summary>
/// The two STS operations credential providers need, as form requests to the regional STS
/// endpoint. Written by hand rather than generated: this package is the runtime that generated AWS
/// clients reference, so it cannot depend on code generation for its own build.
/// </summary>
internal static class AwsSts
{
    private const string Version = "2011-06-15";
    private const string DefaultRegion = "us-east-1";

    /// <summary><c>AssumeRole</c>, signed with <paramref name="source"/>.</summary>
    public static async Task<AwsCredentials> AssumeRoleAsync(
        string providerName,
        HttpClient httpClient,
        TimeProvider timeProvider,
        string? region,
        AwsCredentials source,
        string roleArn,
        string sessionName,
        string? externalId,
        TimeSpan? duration,
        CancellationToken cancellationToken
    )
    {
        var parameters = new List<KeyValuePair<string, string>>
        {
            new("Action", "AssumeRole"),
            new("Version", Version),
            new("RoleArn", roleArn),
            new("RoleSessionName", sessionName),
        };
        if (externalId is not null)
        {
            parameters.Add(new("ExternalId", externalId));
        }
        AddDuration(parameters, duration);

        var resolvedRegion = region ?? DefaultRegion;
        var request = Request(resolvedRegion, parameters);
        await new AwsSigV4Signer("sts", resolvedRegion, timeProvider)
            .SignAsync(new SmithyContext(), request, source, cancellationToken)
            .ConfigureAwait(false);
        return await SendAsync(providerName, httpClient, request, roleArn, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary><c>AssumeRoleWithWebIdentity</c>, which STS accepts unsigned.</summary>
    public static Task<AwsCredentials> AssumeRoleWithWebIdentityAsync(
        string providerName,
        HttpClient httpClient,
        string? region,
        string webIdentityToken,
        string roleArn,
        string sessionName,
        TimeSpan? duration,
        CancellationToken cancellationToken
    )
    {
        var parameters = new List<KeyValuePair<string, string>>
        {
            new("Action", "AssumeRoleWithWebIdentity"),
            new("Version", Version),
            new("RoleArn", roleArn),
            new("RoleSessionName", sessionName),
            new("WebIdentityToken", webIdentityToken),
        };
        AddDuration(parameters, duration);
        return SendAsync(
            providerName,
            httpClient,
            Request(region ?? DefaultRegion, parameters),
            roleArn,
            cancellationToken
        );
    }

    /// <summary>A session name for callers that do not name one.</summary>
    public static string DefaultSessionName(TimeProvider timeProvider) =>
        "nsmithy-"
        + timeProvider.GetUtcNow().ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);

    private static void AddDuration(
        List<KeyValuePair<string, string>> parameters,
        TimeSpan? duration
    )
    {
        if (duration is { } value)
        {
            parameters.Add(
                new(
                    "DurationSeconds",
                    ((long)value.TotalSeconds).ToString(CultureInfo.InvariantCulture)
                )
            );
        }
    }

    private static SmithyHttpRequest Request(
        string region,
        List<KeyValuePair<string, string>> parameters
    )
    {
        var dnsSuffix = AwsEndpointFunctions.Partition(region).DnsSuffix;
        var body = string.Join(
            '&',
            parameters.Select(parameter =>
                Uri.EscapeDataString(parameter.Key) + "=" + Uri.EscapeDataString(parameter.Value)
            )
        );
        return new SmithyHttpRequest(HttpMethod.Post, $"https://sts.{region}.{dnsSuffix}/")
        {
            Body = new SmithyHttpBody.Bytes(Encoding.UTF8.GetBytes(body)),
            ContentType = "application/x-www-form-urlencoded",
        };
    }

    private static async Task<AwsCredentials> SendAsync(
        string providerName,
        HttpClient httpClient,
        SmithyHttpRequest request,
        string roleArn,
        CancellationToken cancellationToken
    )
    {
        SmithyHttpClientResponse response;
        try
        {
            response = await new HttpClientTransport(httpClient)
                .SendAsync(request, SmithyHttpClientResponseMode.Buffer, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            throw new AwsCredentialsProviderException(
                providerName,
                $"STS could not be reached to assume role {roleArn}.",
                innerException: exception
            );
        }

        XDocument document;
        try
        {
            document = XDocument.Parse(Encoding.UTF8.GetString(response.Content));
        }
        catch (XmlException exception)
        {
            throw new AwsCredentialsProviderException(
                providerName,
                $"STS returned HTTP {(int)response.StatusCode} with a body that is not XML while assuming role {roleArn}.",
                innerException: exception
            );
        }

        if ((int)response.StatusCode >= 400)
        {
            var code = Element(document, "Code") ?? "Unknown";
            var message = Element(document, "Message");
            throw new AwsCredentialsProviderException(
                providerName,
                $"STS refused to assume role {roleArn}: {code}{(message is null ? "" : ": " + message)}"
            );
        }

        var accessKeyId = Element(document, "AccessKeyId");
        var secretAccessKey = Element(document, "SecretAccessKey");
        if (accessKeyId is null || secretAccessKey is null)
        {
            throw new AwsCredentialsProviderException(
                providerName,
                $"STS returned no credentials while assuming role {roleArn}."
            );
        }
        var expiration = Element(document, "Expiration");
        return new AwsCredentials(
            accessKeyId,
            secretAccessKey,
            Element(document, "SessionToken"),
            expiration is null
                ? null
                : DateTimeOffset.Parse(
                    expiration,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal
                )
        );
    }

    private static string? Element(XDocument document, string localName) =>
        document
            .Descendants()
            .FirstOrDefault(element => element.Name.LocalName == localName)
            ?.Value;
}
