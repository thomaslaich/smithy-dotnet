using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using NSmithy.Client;
using NSmithy.Http;

namespace NSmithy.Aws;

public sealed class AwsSigV4Middleware(
    Uri endpoint,
    string service,
    string region,
    IAwsCredentialsProvider credentialsProvider,
    TimeProvider? timeProvider = null
) : ISmithyClientMiddleware
{
    private const string Algorithm = "AWS4-HMAC-SHA256";
    private static readonly DateTimeFormatInfo InvariantDateTime = CultureInfo
        .InvariantCulture
        .DateTimeFormat;

    private readonly Uri endpoint = endpoint is { IsAbsoluteUri: true }
        ? endpoint
        : throw new ArgumentException("Endpoint must be an absolute URI.", nameof(endpoint));

    private readonly string service = string.IsNullOrWhiteSpace(service)
        ? throw new ArgumentException("Service must be set.", nameof(service))
        : service;

    private readonly string region = string.IsNullOrWhiteSpace(region)
        ? throw new ArgumentException("Region must be set.", nameof(region))
        : region;

    private readonly IAwsCredentialsProvider credentialsProvider =
        credentialsProvider ?? throw new ArgumentNullException(nameof(credentialsProvider));

    private readonly TimeProvider timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<SmithyOperationResponse> InvokeAsync(
        SmithyOperationRequest request,
        SmithyOperationNext nextOperation,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(nextOperation);

        var credentials = await credentialsProvider
            .GetCredentialsAsync(cancellationToken)
            .ConfigureAwait(false);
        Sign(request.Request, credentials, timeProvider.GetUtcNow());
        return await nextOperation(request, cancellationToken).ConfigureAwait(false);
    }

    internal void Sign(SmithyHttpRequest request, AwsCredentials credentials, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(credentials);

        var requestUri = ResolveRequestUri(request.RequestUri);
        var amzDate = now.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", InvariantDateTime);
        var date = now.UtcDateTime.ToString("yyyyMMdd", InvariantDateTime);
        var payloadHash = Sha256Hex(request.Content ?? []);

        request.Headers["Host"] = [HostHeader(requestUri)];
        request.Headers["X-Amz-Date"] = [amzDate];
        request.Headers["X-Amz-Content-Sha256"] = [payloadHash];
        if (!string.IsNullOrEmpty(credentials.SessionToken))
        {
            request.Headers["X-Amz-Security-Token"] = [credentials.SessionToken];
        }

        var canonicalHeaders = CanonicalHeaders(request);
        var signedHeaders = string.Join(';', canonicalHeaders.Select(h => h.Name));
        var canonicalRequest = string.Join(
            '\n',
            request.Method.Method.ToUpperInvariant(),
            CanonicalPath(requestUri),
            CanonicalQuery(requestUri),
            string.Concat(canonicalHeaders.Select(h => $"{h.Name}:{h.Value}\n")),
            signedHeaders,
            payloadHash
        );

        var credentialScope = $"{date}/{region}/{service}/aws4_request";
        var stringToSign = string.Join(
            '\n',
            Algorithm,
            amzDate,
            credentialScope,
            Sha256Hex(Encoding.UTF8.GetBytes(canonicalRequest))
        );
        var signature = ToHex(
            HmacSha256(
                DeriveSigningKey(credentials.SecretAccessKey, date, region, service),
                stringToSign
            )
        );

        request.Headers["Authorization"] =
        [
            $"{Algorithm} Credential={credentials.AccessKeyId}/{credentialScope}, SignedHeaders={signedHeaders}, Signature={signature}",
        ];
    }

    private Uri ResolveRequestUri(string requestUri)
    {
        if (
            Uri.TryCreate(requestUri, UriKind.Absolute, out var absolute)
            && absolute.IsAbsoluteUri
            && (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps)
        )
        {
            return absolute;
        }

        return new Uri(endpoint, requestUri);
    }

    private static string HostHeader(Uri uri)
    {
        if (uri.IsDefaultPort)
        {
            return uri.Host;
        }

        return string.Create(CultureInfo.InvariantCulture, $"{uri.Host}:{uri.Port}");
    }

    private static (string Name, string Value)[] CanonicalHeaders(SmithyHttpRequest request)
    {
        var headers = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
        AddHeaders(headers, request.Headers);

        if (!string.IsNullOrWhiteSpace(request.ContentType))
        {
            AddHeader(headers, "content-type", request.ContentType);
        }

        AddHeaders(headers, request.ContentHeaders);

        return headers
            .Select(h => (h.Key, string.Join(',', h.Value.Select(NormalizeHeaderValue))))
            .ToArray();
    }

    private static void AddHeaders(
        SortedDictionary<string, List<string>> headers,
        IEnumerable<KeyValuePair<string, IReadOnlyList<string>>> values
    )
    {
        foreach (var header in values)
        {
            foreach (var value in header.Value)
            {
                AddHeader(headers, header.Key, value);
            }
        }
    }

    private static void AddHeader(
        SortedDictionary<string, List<string>> headers,
        string name,
        string value
    )
    {
        var canonicalName = name.Trim().ToLowerInvariant();
        if (!headers.TryGetValue(canonicalName, out var values))
        {
            values = [];
            headers[canonicalName] = values;
        }

        values.Add(value);
    }

    private static string NormalizeHeaderValue(string value)
    {
        return string.Join(
            ' ',
            value.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
        );
    }

    private static string CanonicalPath(Uri uri)
    {
        var path = string.IsNullOrEmpty(uri.AbsolutePath) ? "/" : uri.AbsolutePath;
        return string.Join('/', path.Split('/').Select(EscapePathSegment));
    }

    private static string EscapePathSegment(string segment)
    {
        return string.Concat(
            Encoding.UTF8.GetBytes(Uri.UnescapeDataString(segment)).Select(EscapeByte)
        );
    }

    private static string CanonicalQuery(Uri uri)
    {
        if (string.IsNullOrEmpty(uri.Query) || uri.Query == "?")
        {
            return "";
        }

        return string.Join(
            '&',
            uri.Query[1..]
                .Split('&', StringSplitOptions.None)
                .Select(part =>
                {
                    var equals = part.IndexOf('=', StringComparison.Ordinal);
                    var name = equals >= 0 ? part[..equals] : part;
                    var value = equals >= 0 ? part[(equals + 1)..] : "";
                    return (
                        Name: EscapeQueryComponent(
                            Uri.UnescapeDataString(
                                name.Replace("+", "%20", StringComparison.Ordinal)
                            )
                        ),
                        Value: EscapeQueryComponent(
                            Uri.UnescapeDataString(
                                value.Replace("+", "%20", StringComparison.Ordinal)
                            )
                        )
                    );
                })
                .OrderBy(p => p.Name, StringComparer.Ordinal)
                .ThenBy(p => p.Value, StringComparer.Ordinal)
                .Select(p => $"{p.Name}={p.Value}")
        );
    }

    private static string EscapeQueryComponent(string value)
    {
        return string.Concat(Encoding.UTF8.GetBytes(value).Select(EscapeByte));
    }

    private static string EscapeByte(byte value)
    {
        var c = (char)value;
        if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
        {
            return c.ToString();
        }

        return c is '-' or '_' or '.' or '~'
            ? c.ToString()
            : $"%{value.ToString("X2", CultureInfo.InvariantCulture)}";
    }

    private static string Sha256Hex(byte[] bytes)
    {
        return ToHex(SHA256.HashData(bytes));
    }

    private static byte[] DeriveSigningKey(
        string secretAccessKey,
        string date,
        string region,
        string service
    )
    {
        var dateKey = HmacSha256(Encoding.UTF8.GetBytes("AWS4" + secretAccessKey), date);
        var regionKey = HmacSha256(dateKey, region);
        var serviceKey = HmacSha256(regionKey, service);
        return HmacSha256(serviceKey, "aws4_request");
    }

    private static byte[] HmacSha256(byte[] key, string value)
    {
        return HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(value));
    }

    private static string ToHex(byte[] bytes)
    {
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
