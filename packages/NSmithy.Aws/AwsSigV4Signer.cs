using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using NSmithy.Client;
using NSmithy.Http;

namespace NSmithy.Aws;

/// <summary>
/// Signs HTTP requests with AWS Signature Version 4. The signing name and region come from the
/// resolved endpoint's SigV4 auth scheme properties, falling back to the values this signer is
/// constructed with.
/// </summary>
public sealed class AwsSigV4Signer(
    string? service,
    string? region,
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

    private readonly string? service =
        service is not null && string.IsNullOrWhiteSpace(service)
            ? throw new ArgumentException("Service must not be empty.", nameof(service))
            : service;

    private readonly string? region =
        region is not null && string.IsNullOrWhiteSpace(region)
            ? throw new ArgumentException("Region must not be empty.", nameof(region))
            : region;

    private readonly TimeProvider timeProvider = timeProvider ?? TimeProvider.System;

    /// <summary>
    /// A signer that takes its signing name and region from the resolved endpoint only, as the
    /// endpoints of services with modeled endpoint rules always set them.
    /// </summary>
    public AwsSigV4Signer(TimeProvider? timeProvider = null)
        : this(null, null, timeProvider) { }

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
        Sign(request, credentials, timeProvider.GetUtcNow(), ResolveSigningOptions(context));
        return ValueTask.FromResult(request);
    }

    /// <summary>
    /// The signing options for one request: the resolved endpoint's SigV4 auth scheme properties
    /// where it sets them, otherwise the values this signer was constructed with.
    /// </summary>
    private SigningOptions ResolveSigningOptions(SmithyContext context)
    {
        SmithyEndpointAuthScheme? properties = null;
        if (
            context.TryGet(SmithyContextKeys.ResolvedEndpoint, out var endpoint)
            && endpoint?.AuthSchemes is { } authSchemes
        )
        {
            properties = authSchemes.FirstOrDefault(authScheme =>
                string.Equals(authScheme.SchemeId, AwsSigV4AuthScheme.Id, StringComparison.Ordinal)
            );
        }

        var signingName =
            properties?.GetString("signingName")
            ?? service
            ?? throw new InvalidOperationException(
                "AWS SigV4 needs a signing name: the resolved endpoint sets none, so construct the "
                    + "auth scheme with the service's signing name."
            );
        var signingRegion =
            properties?.GetString("signingRegion")
            ?? region
            ?? throw new InvalidOperationException(
                "AWS SigV4 needs a signing region: set the client's Region, or construct the auth "
                    + "scheme with a region."
            );
        var s3 = UsesS3PathRules(signingName);
        return new SigningOptions(
            signingName,
            signingRegion,
            properties?.GetBoolean("disableDoubleEncoding") ?? s3,
            properties?.GetBoolean("disableNormalizePath") ?? s3
        );
    }

    private SigningOptions DefaultSigningOptions()
    {
        if (service is null || region is null)
        {
            throw new InvalidOperationException(
                "AWS SigV4 needs a signing name and region: construct the signer with both."
            );
        }
        var s3 = UsesS3PathRules(service);
        return new SigningOptions(service, region, s3, s3);
    }

    // S3 signs the path as sent: encoded once and not normalized. Its endpoint rules say so through
    // the disableDoubleEncoding and disableNormalizePath properties; this covers requests signed
    // without them.
    private static bool UsesS3PathRules(string signingName) =>
        signingName is "s3" or "s3-outposts" or "s3express";

    internal void Sign(SmithyHttpRequest request, AwsCredentials credentials, DateTimeOffset now) =>
        Sign(request, credentials, now, DefaultSigningOptions());

    private static void Sign(
        SmithyHttpRequest request,
        AwsCredentials credentials,
        DateTimeOffset now,
        SigningOptions options
    )
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
            CanonicalPath(requestUri, options),
            CanonicalQuery(requestUri),
            string.Concat(canonicalHeaders.Select(h => $"{h.Name}:{h.Value}\n")),
            signedHeaders,
            payloadHash
        );

        var credentialScope = $"{date}/{options.Region}/{options.Service}/aws4_request";
        var stringToSign = string.Join(
            '\n',
            Algorithm,
            amzDate,
            credentialScope,
            Sha256Hex(Encoding.UTF8.GetBytes(canonicalRequest))
        );
        var signature = ToHex(
            HmacSha256(
                DeriveSigningKey(
                    credentials.SecretAccessKey,
                    date,
                    options.Region,
                    options.Service
                ),
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

        var options = DefaultSigningOptions();
        request.Headers.Remove("Authorization");
        request.Headers.Remove("X-Amz-Date");
        request.Headers.Remove("X-Amz-Security-Token");
        var requestUri = ResolveRequestUri(request.RequestUri);
        request.Headers["Host"] = [HostHeader(requestUri)];

        var amzDate = now.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", InvariantDateTime);
        var date = now.UtcDateTime.ToString("yyyyMMdd", InvariantDateTime);
        var credentialScope = $"{date}/{options.Region}/{options.Service}/aws4_request";
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
            CanonicalPath(unsignedUri, options),
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
                DeriveSigningKey(
                    credentials.SecretAccessKey,
                    date,
                    options.Region,
                    options.Service
                ),
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

    private static Uri ResolveRequestUri(string requestUri) =>
        Uri.TryCreate(requestUri, UriKind.Absolute, out var absolute)
        && (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps)
            ? absolute
            : throw new InvalidOperationException(
                "AWS SigV4 requires an absolute request URI after endpoint resolution."
            );

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

    /// <summary>
    /// The canonical URI path. Unless disabled, the path is normalized (empty and <c>.</c>
    /// segments removed, <c>..</c> segments resolved) and each segment is encoded a second time
    /// over its wire encoding.
    /// </summary>
    private static string CanonicalPath(Uri uri, SigningOptions options)
    {
        var path = string.IsNullOrEmpty(uri.AbsolutePath) ? "/" : uri.AbsolutePath;
        var segments = path.Split('/').Select(EscapePathSegment);
        if (!options.DisableDoubleEncoding)
        {
            segments = segments.Select(EscapeQueryComponent);
        }
        if (options.DisableNormalizePath)
        {
            return string.Join('/', segments);
        }

        var normalized = new List<string>();
        foreach (var segment in segments)
        {
            if (segment is "" or ".")
            {
                continue;
            }
            if (segment == "..")
            {
                if (normalized.Count > 0)
                {
                    normalized.RemoveAt(normalized.Count - 1);
                }
                continue;
            }
            normalized.Add(segment);
        }
        var canonical = "/" + string.Join('/', normalized);
        return path.EndsWith('/') && normalized.Count > 0 ? canonical + "/" : canonical;
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

    private readonly record struct SigningOptions(
        string Service,
        string Region,
        bool DisableDoubleEncoding,
        bool DisableNormalizePath
    );
}
