using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using NSmithy.Client;
using NSmithy.Http;

namespace NSmithy.Aws;

/// <summary>Signs HTTP requests with AWS Signature Version 4.</summary>
public sealed class AwsSigV4Signer(
    Uri? endpoint,
    string service,
    string region,
    TimeProvider? timeProvider = null
) : ISmithySigner
{
    private const string Algorithm = "AWS4-HMAC-SHA256";
    private static readonly HashSet<string> PresignAuthenticationParameters = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        "X-Amz-Algorithm",
        "X-Amz-Credential",
        "X-Amz-Date",
        "X-Amz-Expires",
        "X-Amz-Signature",
        "X-Amz-SignedHeaders",
        "X-Amz-Security-Token",
    };
    private static readonly DateTimeFormatInfo InvariantDateTime = CultureInfo
        .InvariantCulture
        .DateTimeFormat;

    private readonly Uri? endpoint =
        endpoint is null || endpoint.IsAbsoluteUri
            ? endpoint
            : throw new ArgumentException("Endpoint must be an absolute URI.", nameof(endpoint));

    private readonly string service = string.IsNullOrWhiteSpace(service)
        ? throw new ArgumentException("Service must be set.", nameof(service))
        : service;

    private readonly string region = string.IsNullOrWhiteSpace(region)
        ? throw new ArgumentException("Region must be set.", nameof(region))
        : region;

    private readonly TimeProvider timeProvider = timeProvider ?? TimeProvider.System;

    public AwsSigV4Signer(string service, string region, TimeProvider? timeProvider = null)
        : this(null, service, region, timeProvider) { }

    public ValueTask<SmithyHttpRequest> SignAsync(
        SmithyContext context,
        SmithyHttpRequest request,
        ISmithyIdentity identity,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        var credentials =
            identity as AwsCredentials
            ?? throw new ArgumentException(
                $"AWS SigV4 requires an {nameof(AwsCredentials)} identity.",
                nameof(identity)
            );
        Sign(request, credentials, timeProvider.GetUtcNow());
        return ValueTask.FromResult(request);
    }

    internal void Sign(SmithyHttpRequest request, AwsCredentials credentials, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(credentials);

        // A retry or caller may hand the signer a previously signed request. Authentication
        // material is output, never canonical input; remove it before rebuilding the signature.
        request.Headers.Remove("Authorization");
        var requestUri = ResolveRequestUri(request.RequestUri);
        var amzDate = now.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", InvariantDateTime);
        var date = now.UtcDateTime.ToString("yyyyMMdd", InvariantDateTime);
        var payloadHash = Sha256Hex(SignablePayloadBytes(request.Body));

        request.Headers["Host"] = [HostHeader(requestUri)];
        request.Headers["X-Amz-Date"] = [amzDate];
        request.Headers["X-Amz-Content-Sha256"] = [payloadHash];
        if (!string.IsNullOrEmpty(credentials.SessionToken))
        {
            request.Headers["X-Amz-Security-Token"] = [credentials.SessionToken];
        }
        else
        {
            request.Headers.Remove("X-Amz-Security-Token");
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

    /// <summary>
    /// Adds SigV4 authentication query parameters to a request and returns its absolute presigned
    /// URI. The request is mutated so it can be sent directly after this call.
    /// </summary>
    /// <remarks>
    /// Presigning uses <c>UNSIGNED-PAYLOAD</c>, the standard AWS query-signing behavior. AWS caps
    /// SigV4 presigned URLs at seven days because the derived signing key is date-scoped.
    /// </remarks>
    public Uri Presign(SmithyHttpRequest request, AwsCredentials credentials, TimeSpan expires) =>
        Presign(request, credentials, expires, timeProvider.GetUtcNow());

    internal Uri Presign(
        SmithyHttpRequest request,
        AwsCredentials credentials,
        TimeSpan expires,
        DateTimeOffset now
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(credentials);
        if (expires < TimeSpan.FromSeconds(1) || expires > TimeSpan.FromDays(7))
        {
            throw new ArgumentOutOfRangeException(
                nameof(expires),
                expires,
                "SigV4 presigning duration must be between one second and seven days."
            );
        }

        request.Headers.Remove("Authorization");
        request.Headers.Remove("X-Amz-Date");
        request.Headers.Remove("X-Amz-Security-Token");
        var requestUri = ResolveRequestUri(request.RequestUri);
        request.Headers["Host"] = [HostHeader(requestUri)];

        var amzDate = now.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", InvariantDateTime);
        var date = now.UtcDateTime.ToString("yyyyMMdd", InvariantDateTime);
        var credentialScope = $"{date}/{region}/{service}/aws4_request";
        var canonicalHeaders = CanonicalHeaders(request);
        var signedHeaders = string.Join(';', canonicalHeaders.Select(header => header.Name));

        var query = QueryWithoutAuthentication(requestUri);
        AddQueryParameter(query, "X-Amz-Algorithm", Algorithm);
        AddQueryParameter(
            query,
            "X-Amz-Credential",
            $"{credentials.AccessKeyId}/{credentialScope}"
        );
        AddQueryParameter(query, "X-Amz-Date", amzDate);
        AddQueryParameter(
            query,
            "X-Amz-Expires",
            ((long)expires.TotalSeconds).ToString(CultureInfo.InvariantCulture)
        );
        AddQueryParameter(query, "X-Amz-SignedHeaders", signedHeaders);
        if (!string.IsNullOrEmpty(credentials.SessionToken))
        {
            AddQueryParameter(query, "X-Amz-Security-Token", credentials.SessionToken);
        }

        var unsignedUri = WithQuery(requestUri, query);
        var canonicalRequest = string.Join(
            '\n',
            request.Method.Method.ToUpperInvariant(),
            CanonicalPath(unsignedUri),
            CanonicalQuery(unsignedUri),
            string.Concat(canonicalHeaders.Select(header => $"{header.Name}:{header.Value}\n")),
            signedHeaders,
            "UNSIGNED-PAYLOAD"
        );
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
        AddQueryParameter(query, "X-Amz-Signature", signature);

        var presigned = WithQuery(requestUri, query);
        request.RequestUri = presigned.AbsoluteUri;
        return presigned;
    }

    private static List<KeyValuePair<string, string>> QueryWithoutAuthentication(Uri uri)
    {
        var result = new List<KeyValuePair<string, string>>();
        if (string.IsNullOrEmpty(uri.Query) || uri.Query == "?")
        {
            return result;
        }

        foreach (var part in uri.Query[1..].Split('&', StringSplitOptions.None))
        {
            var equals = part.IndexOf('=', StringComparison.Ordinal);
            var name = equals >= 0 ? part[..equals] : part;
            var decodedName = Uri.UnescapeDataString(name);
            if (PresignAuthenticationParameters.Contains(decodedName))
            {
                continue;
            }
            result.Add(
                new KeyValuePair<string, string>(name, equals >= 0 ? part[(equals + 1)..] : "")
            );
        }
        return result;
    }

    private static void AddQueryParameter(
        List<KeyValuePair<string, string>> query,
        string name,
        string value
    ) =>
        query.Add(
            new KeyValuePair<string, string>(
                EscapeQueryComponent(name),
                EscapeQueryComponent(value)
            )
        );

    private static Uri WithQuery(Uri uri, IEnumerable<KeyValuePair<string, string>> query)
    {
        var builder = new UriBuilder(uri)
        {
            Query = string.Join(
                '&',
                query.Select(parameter => $"{parameter.Key}={parameter.Value}")
            ),
        };
        return builder.Uri;
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

        if (endpoint is null)
        {
            throw new InvalidOperationException(
                "AWS SigV4 requires an absolute request URI after endpoint resolution."
            );
        }

        return new Uri(endpoint, requestUri);
    }

    private static byte[] SignablePayloadBytes(SmithyHttpBody body) =>
        body switch
        {
            SmithyHttpBody.Bytes bytes => bytes.Content,
            SmithyHttpBody.Streaming => throw new InvalidOperationException(
                "AWS SigV4 signing for streaming request bodies is not supported yet."
            ),
            _ => [],
        };

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
