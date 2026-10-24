using NSmithy.Client;

namespace NSmithy.Tests.Client;

// Vectors from smithy-rules-engine-tests (parse-url, substring, url-encode, valid-hostlabel) and
// the split examples of the rules engine's reference implementation.
public sealed class EndpointFunctionsTests
{
    [Theory]
    [InlineData(
        "https://authority.com/custom-path",
        "https",
        "authority.com",
        "/custom-path",
        "/custom-path/",
        false
    )]
    [InlineData("https://authority.com", "https", "authority.com", "", "/", false)]
    [InlineData("https://authority.com/", "https", "authority.com", "/", "/", false)]
    [InlineData(
        "http://authority.com:8000/port",
        "http",
        "authority.com:8000",
        "/port",
        "/port/",
        false
    )]
    [InlineData("http://99_ab.com", "http", "99_ab.com", "", "/", false)]
    [InlineData("http://99_ab-.com", "http", "99_ab-.com", "", "/", false)]
    [InlineData("http://192.168.1.1/foo/", "http", "192.168.1.1", "/foo/", "/foo/", true)]
    [InlineData("http://192.168.1.1:1234/foo/", "http", "192.168.1.1:1234", "/foo/", "/foo/", true)]
    [InlineData(
        "https://[2001:db8:85a3:8d3:1319:8a2e:370:7348]:443",
        "https",
        "[2001:db8:85a3:8d3:1319:8a2e:370:7348]:443",
        "",
        "/",
        true
    )]
    [InlineData("https://999.999.abc.blah", "https", "999.999.abc.blah", "", "/", false)]
    public void ParseUrlSplitsValidUrls(
        string url,
        string scheme,
        string authority,
        string path,
        string normalizedPath,
        bool isIp
    )
    {
        Assert.Equal(
            new EndpointUrl(scheme, authority, path, normalizedPath, isIp),
            EndpointFunctions.ParseUrl(url)
        );
    }

    [Theory]
    [InlineData("acbd://example.com")]
    [InlineData("http://abc.com:a/foo")]
    [InlineData("https://example.com/path?query1=foo")]
    [InlineData("example.com")]
    public void ParseUrlRejectsInvalidUrls(string url)
    {
        Assert.Null(EndpointFunctions.ParseUrl(url));
    }

    [Theory]
    [InlineData("abcdefg", 0, 4, false, "abcd")]
    [InlineData("abcd", 0, 4, false, "abcd")]
    [InlineData("abc", 0, 4, false, null)]
    [InlineData("", 0, 4, false, null)]
    [InlineData("﷽", 0, 4, false, null)]
    [InlineData("\u007Fabcdef", 0, 4, false, "\u007Fabc")]
    [InlineData("abcdefg", 0, 4, true, "defg")]
    [InlineData("defg", 0, 4, true, "defg")]
    [InlineData("abc", 0, 4, true, null)]
    [InlineData("defg", 1, 3, false, "ef")]
    [InlineData("def", 1, 3, false, "ef")]
    [InlineData("ab", 1, 3, false, null)]
    public void SubstringMatchesTheRulesEngine(
        string value,
        int start,
        int stop,
        bool reverse,
        string? expected
    )
    {
        Assert.Equal(expected, EndpointFunctions.Substring(value, start, stop, reverse));
    }

    [Theory]
    [InlineData("abcdefg", "abcdefg")]
    [InlineData("abc:defg", "abc%3Adefg")]
    [InlineData(
        "/:,?#[]{}|@! $&'()*+;=%<>\"^`\\",
        "%2F%3A%2C%3F%23%5B%5D%7B%7D%7C%40%21%20%24%26%27%28%29%2A%2B%3B%3D%25%3C%3E%22%5E%60%5C"
    )]
    [InlineData("0123456789.underscore_dash-Tilda~", "0123456789.underscore_dash-Tilda~")]
    [InlineData("😹", "%F0%9F%98%B9")]
    [InlineData("", "")]
    public void UriEncodeEscapesAllButUnreservedCharacters(string value, string expected)
    {
        Assert.Equal(expected, EndpointFunctions.UriEncode(value));
    }

    [Theory]
    [InlineData("us-east-1", false, true)]
    [InlineData("3aws4", false, true)]
    [InlineData("part1.part2", false, false)]
    [InlineData("part1.part2", true, true)]
    [InlineData("part1 part2", true, false)]
    [InlineData("", true, false)]
    [InlineData("part1.", true, false)]
    [InlineData("part1..part2", true, false)]
    [InlineData("part1.-part2", true, false)]
    [InlineData("-part1", false, false)]
    public void IsValidHostLabelMatchesTheRulesEngine(
        string value,
        bool allowSubDomains,
        bool expected
    )
    {
        Assert.Equal(expected, EndpointFunctions.IsValidHostLabel(value, allowSubDomains));
    }

    [Theory]
    [InlineData("a--b--c", "--", 0, new[] { "a", "b", "c" })]
    [InlineData("a--b--c", "--", 2, new[] { "a", "b--c" })]
    [InlineData("--b--", "--", 0, new[] { "", "b", "" })]
    [InlineData("abc", "x", 0, new[] { "abc" })]
    [InlineData("", "--", 0, new[] { "" })]
    [InlineData("----", "--", 0, new[] { "", "", "" })]
    [InlineData("a--b--c--d", "--", 3, new[] { "a", "b", "c--d" })]
    [InlineData("--", "--", 0, new[] { "", "" })]
    [InlineData("mybucket", "--", 1, new[] { "mybucket" })]
    [InlineData("--x-s3--azid--suffix", "--", 0, new[] { "", "x-s3", "azid", "suffix" })]
    [InlineData("--x-s3--azid--suffix", "--", 4, new[] { "", "x-s3", "azid", "suffix" })]
    [InlineData("--x-s3--azid--suffix", "--", 2, new[] { "", "x-s3--azid--suffix" })]
    public void SplitMatchesTheRulesEngine(
        string value,
        string delimiter,
        int limit,
        string[] expected
    )
    {
        Assert.Equal(expected, EndpointFunctions.Split(value, delimiter, limit));
    }

    [Theory]
    [InlineData(0, "a")]
    [InlineData(2, "c")]
    [InlineData(-1, "c")]
    [InlineData(-3, "a")]
    [InlineData(3, null)]
    [InlineData(-4, null)]
    public void IndexCountsNegativeIndexesFromTheEnd(int index, string? expected)
    {
        Assert.Equal(expected, EndpointFunctions.Index(["a", "b", "c"], index));
    }
}
