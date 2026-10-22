namespace NSmithy.Aws;

/// <summary>Adapts modeled S3 HTTP paths to endpoints produced by S3 endpoint rules.</summary>
public static class AwsS3EndpointPath
{
    /// <summary>
    /// Removes the leading serialized Bucket label. Rule endpoints already place the bucket in
    /// the authority or base path. Leaves the key's wire encoding and any query string intact.
    /// Called by generated resolvers only for operations whose modeled path starts with Bucket.
    /// </summary>
    public static string RemoveBucket(string requestPath)
    {
        ArgumentNullException.ThrowIfNull(requestPath);
        var start = requestPath.StartsWith('/') ? 1 : 0;
        var end = requestPath.IndexOfAny(['/', '?'], start);
        if (end < 0)
        {
            return "/";
        }
        return requestPath[end] == '/' ? requestPath[end..] : "/" + requestPath[end..];
    }
}
