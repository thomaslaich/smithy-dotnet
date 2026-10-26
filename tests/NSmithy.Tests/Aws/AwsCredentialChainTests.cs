using System.Net;
using System.Text;
using NSmithy.Aws;

namespace NSmithy.Tests.Aws;

public sealed class AwsCredentialChainTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    // ---------------- region ----------------

    [Fact]
    public void RegionComesFromAwsRegionFirst()
    {
        var region = AwsRegion.Resolve(
            Environment(("AWS_REGION", "eu-central-1"), ("AWS_DEFAULT_REGION", "us-west-2"))
        );

        Assert.Equal("eu-central-1", region);
    }

    [Fact]
    public void RegionFallsBackToAwsDefaultRegion()
    {
        Assert.Equal(
            "us-west-2",
            AwsRegion.Resolve(Environment(("AWS_DEFAULT_REGION", "us-west-2")))
        );
    }

    [Fact]
    public void RegionFallsBackToTheSelectedProfile()
    {
        using var directory = new TemporaryDirectory();
        var config = directory.Write(
            "config",
            "[default]\nregion = us-east-1\n[profile dev]\nregion = ap-southeast-2\n"
        );

        var region = AwsRegion.Resolve(Environment(("AWS_PROFILE", "dev")), config);

        Assert.Equal("ap-southeast-2", region);
    }

    [Fact]
    public void RegionIsNullWhenNothingSetsIt()
    {
        using var directory = new TemporaryDirectory();

        Assert.Null(AwsRegion.Resolve(Environment(), Path.Combine(directory.Path, "missing")));
    }

    // ---------------- web identity ----------------

    [Fact]
    public async Task WebIdentityIsNotConfiguredWithoutItsVariables()
    {
        var provider = new WebIdentityAwsCredentialsProvider(getEnvironmentVariable: Environment());

        var error = await Assert.ThrowsAsync<AwsCredentialsProviderException>(async () =>
            await provider.GetCredentialsAsync()
        );

        Assert.True(error.IsNotConfigured);
    }

    [Fact]
    public async Task WebIdentityExchangesTheTokenFileUnsigned()
    {
        using var directory = new TemporaryDirectory();
        var tokenFile = directory.Write("token", "oidc-token\n");
        var sts = new FakeSts();
        using var httpClient = new HttpClient(sts);
        var provider = new WebIdentityAwsCredentialsProvider(
            httpClient: httpClient,
            getEnvironmentVariable: Environment(
                ("AWS_WEB_IDENTITY_TOKEN_FILE", tokenFile),
                ("AWS_ROLE_ARN", "arn:aws:iam::123456789012:role/ci"),
                ("AWS_ROLE_SESSION_NAME", "build-42"),
                ("AWS_REGION", "eu-west-1")
            )
        );

        var credentials = await provider.GetCredentialsAsync();

        var request = Assert.Single(sts.Requests);
        Assert.Equal("https://sts.eu-west-1.amazonaws.com/", request.Uri);
        Assert.Equal("AssumeRoleWithWebIdentity", request.Form["Action"]);
        Assert.Equal("oidc-token", request.Form["WebIdentityToken"]);
        Assert.Equal("build-42", request.Form["RoleSessionName"]);
        Assert.Null(request.Authorization);
        Assert.Equal("ASIA-ci", credentials.AccessKeyId);
        Assert.Equal("session-ci", credentials.SessionToken);
        Assert.Equal(
            new DateTimeOffset(2026, 10, 25, 13, 0, 0, TimeSpan.Zero),
            credentials.Expiration
        );
    }

    // ---------------- assume role ----------------

    [Fact]
    public async Task AssumeRoleSignsWithTheSourceCredentials()
    {
        var sts = new FakeSts();
        using var httpClient = new HttpClient(sts);
        var provider = new AssumeRoleAwsCredentialsProvider(
            new StaticAwsCredentialsProvider(new AwsCredentials("AKIDSOURCE", "source-secret")),
            "arn:aws:iam::123456789012:role/deploy",
            sessionName: "deploy",
            externalId: "partner-7",
            duration: TimeSpan.FromMinutes(30),
            region: "us-west-2",
            httpClient: httpClient,
            timeProvider: new FixedTimeProvider(Now)
        );

        var credentials = await provider.GetCredentialsAsync();

        var request = Assert.Single(sts.Requests);
        Assert.Equal("https://sts.us-west-2.amazonaws.com/", request.Uri);
        Assert.Equal("AssumeRole", request.Form["Action"]);
        Assert.Equal("partner-7", request.Form["ExternalId"]);
        Assert.Equal("1800", request.Form["DurationSeconds"]);
        Assert.StartsWith(
            "AWS4-HMAC-SHA256 Credential=AKIDSOURCE/20261025/us-west-2/sts/aws4_request",
            request.Authorization,
            StringComparison.Ordinal
        );
        Assert.Equal("ASIA-deploy", credentials.AccessKeyId);
    }

    [Fact]
    public async Task AnStsErrorNamesItsCode()
    {
        var sts = new FakeSts { Deny = true };
        using var httpClient = new HttpClient(sts);
        var provider = new AssumeRoleAwsCredentialsProvider(
            new StaticAwsCredentialsProvider(new AwsCredentials("AKIDSOURCE", "source-secret")),
            "arn:aws:iam::123456789012:role/deploy",
            region: "us-east-1",
            httpClient: httpClient
        );

        var error = await Assert.ThrowsAsync<AwsCredentialsProviderException>(async () =>
            await provider.GetCredentialsAsync()
        );

        Assert.False(error.IsNotConfigured);
        Assert.Contains("AccessDenied", error.Message, StringComparison.Ordinal);
    }

    // ---------------- profiles ----------------

    [Fact]
    public async Task ProfileRolesChainThroughSourceProfiles()
    {
        using var directory = new TemporaryDirectory();
        var config = directory.Write(
            "config",
            """
            [profile admin]
            role_arn = arn:aws:iam::123456789012:role/admin
            source_profile = jump
            region = eu-west-1

            [profile jump]
            role_arn = arn:aws:iam::123456789012:role/jump
            source_profile = base
            """
        );
        var credentials = directory.Write(
            "credentials",
            "[base]\naws_access_key_id = AKIDBASE\naws_secret_access_key = base-secret\n"
        );
        var sts = new FakeSts();
        using var httpClient = new HttpClient(sts);
        var provider = Profile("admin", config, credentials, httpClient);

        var result = await provider.GetCredentialsAsync();

        Assert.Equal(2, sts.Requests.Count);
        Assert.Equal("arn:aws:iam::123456789012:role/jump", sts.Requests[0].Form["RoleArn"]);
        Assert.Contains(
            "Credential=AKIDBASE/",
            sts.Requests[0].Authorization,
            StringComparison.Ordinal
        );
        Assert.Equal("arn:aws:iam::123456789012:role/admin", sts.Requests[1].Form["RoleArn"]);
        Assert.Contains(
            "Credential=ASIA-jump/",
            sts.Requests[1].Authorization,
            StringComparison.Ordinal
        );
        Assert.Equal("https://sts.eu-west-1.amazonaws.com/", sts.Requests[1].Uri);
        Assert.Equal("ASIA-admin", result.AccessKeyId);
    }

    [Fact]
    public async Task AProfileThatSourcesItselfUsesItsOwnKeys()
    {
        using var directory = new TemporaryDirectory();
        var credentials = directory.Write(
            "credentials",
            """
            [ops]
            aws_access_key_id = AKIDOPS
            aws_secret_access_key = ops-secret
            role_arn = arn:aws:iam::123456789012:role/ops
            source_profile = ops
            """
        );
        var sts = new FakeSts();
        using var httpClient = new HttpClient(sts);

        var result = await Profile("ops", directory.Missing, credentials, httpClient)
            .GetCredentialsAsync();

        Assert.Contains(
            "Credential=AKIDOPS/",
            Assert.Single(sts.Requests).Authorization,
            StringComparison.Ordinal
        );
        Assert.Equal("ASIA-ops", result.AccessKeyId);
    }

    [Fact]
    public async Task ASourceProfileCycleFailsWithThePath()
    {
        using var directory = new TemporaryDirectory();
        var config = directory.Write(
            "config",
            """
            [profile a]
            role_arn = arn:aws:iam::123456789012:role/a
            source_profile = b

            [profile b]
            role_arn = arn:aws:iam::123456789012:role/b
            source_profile = a
            """
        );

        var error = await Assert.ThrowsAsync<AwsCredentialsProviderException>(async () =>
            await Profile("a", config, directory.Missing).GetCredentialsAsync()
        );

        Assert.Contains("a -> b -> a", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ACredentialSourceSignsTheRoleWithTheEnvironment()
    {
        using var directory = new TemporaryDirectory();
        var config = directory.Write(
            "config",
            "[default]\nrole_arn = arn:aws:iam::123456789012:role/env\ncredential_source = Environment\n"
        );
        var sts = new FakeSts();
        using var httpClient = new HttpClient(sts);
        var provider = new ProfileAwsCredentialsProvider(
            credentialsPath: directory.Missing,
            configPath: config,
            httpClient: httpClient,
            getEnvironmentVariable: Environment(
                ("AWS_ACCESS_KEY_ID", "AKIDENV"),
                ("AWS_SECRET_ACCESS_KEY", "env-secret")
            )
        );

        await provider.GetCredentialsAsync();

        Assert.Contains(
            "Credential=AKIDENV/",
            Assert.Single(sts.Requests).Authorization,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task AProfileWebIdentityTokenFileAssumesTheRole()
    {
        using var directory = new TemporaryDirectory();
        var token = directory.Write("token", "profile-token");
        var config = directory.Write(
            "config",
            $"[default]\nrole_arn = arn:aws:iam::123456789012:role/web\nweb_identity_token_file = {token}\n"
        );
        var sts = new FakeSts();
        using var httpClient = new HttpClient(sts);

        var result = await Profile("default", config, directory.Missing, httpClient)
            .GetCredentialsAsync();

        var request = Assert.Single(sts.Requests);
        Assert.Equal("AssumeRoleWithWebIdentity", request.Form["Action"]);
        Assert.Equal("profile-token", request.Form["WebIdentityToken"]);
        Assert.Equal("ASIA-web", result.AccessKeyId);
    }

    [Fact]
    public async Task ACredentialProcessRunsWithoutAShell()
    {
        using var directory = new TemporaryDirectory();
        var config = directory.Write(
            "config",
            "[default]\ncredential_process = /opt/bin/creds --profile 'my team' \"a b\"\n"
        );
        IReadOnlyList<string>? ran = null;
        var provider = new ProfileAwsCredentialsProvider(
            credentialsPath: directory.Missing,
            configPath: config,
            getEnvironmentVariable: Environment(),
            runProcess: (arguments, _) =>
            {
                ran = arguments;
                return Task.FromResult(
                    new ProcessAwsCredentialsResult(
                        0,
                        """{"Version":1,"AccessKeyId":"AKIDPROC","SecretAccessKey":"proc-secret","SessionToken":"proc-token","Expiration":"2026-10-25T13:00:00Z"}""",
                        ""
                    )
                );
            }
        );

        var result = await provider.GetCredentialsAsync();

        Assert.Equal(["/opt/bin/creds", "--profile", "my team", "a b"], ran);
        Assert.Equal("AKIDPROC", result.AccessKeyId);
        Assert.Equal("proc-token", result.SessionToken);
        Assert.NotNull(result.Expiration);
    }

    [Fact]
    public async Task AFailingCredentialProcessReportsItsError()
    {
        var provider = new ProcessAwsCredentialsProvider(
            "creds",
            (_, _) => Task.FromResult(new ProcessAwsCredentialsResult(3, "", "token expired\n"))
        );

        var error = await Assert.ThrowsAsync<AwsCredentialsProviderException>(async () =>
            await provider.GetCredentialsAsync()
        );

        Assert.Contains(
            "exited with code 3: token expired",
            error.Message,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task MfaProfilesFailWithAClearMessage()
    {
        using var directory = new TemporaryDirectory();
        var config = directory.Write(
            "config",
            "[default]\nrole_arn = arn:aws:iam::123456789012:role/mfa\nsource_profile = default\nmfa_serial = arn:aws:iam::123456789012:mfa/me\n"
        );

        var error = await Assert.ThrowsAsync<AwsCredentialsProviderException>(async () =>
            await Profile("default", config, directory.Missing).GetCredentialsAsync()
        );

        Assert.Contains("mfa_serial", error.Message, StringComparison.Ordinal);
    }

    // ---------------- container ----------------

    [Fact]
    public async Task ContainerCredentialsUseTheEcsHostAndToken()
    {
        HttpRequestMessage? sent = null;
        using var httpClient = new HttpClient(
            new Handler(request =>
            {
                sent = request;
                return Json(
                    """{"AccessKeyId":"AKIDECS","SecretAccessKey":"ecs-secret","Token":"ecs-token","Expiration":"2026-10-25T13:00:00Z"}"""
                );
            })
        );
        var provider = new ContainerAwsCredentialsProvider(
            httpClient,
            Environment(
                ("AWS_CONTAINER_CREDENTIALS_RELATIVE_URI", "/v2/credentials/abc"),
                ("AWS_CONTAINER_AUTHORIZATION_TOKEN", "Basic secret")
            )
        );

        var result = await provider.GetCredentialsAsync();

        Assert.Equal("http://169.254.170.2/v2/credentials/abc", sent!.RequestUri!.AbsoluteUri);
        Assert.Equal("Basic secret", sent.Headers.GetValues("Authorization").Single());
        Assert.Equal("ecs-token", result.SessionToken);
    }

    [Theory]
    [InlineData("http://169.254.170.23/v1/credentials", true)]
    [InlineData("http://[fd00:ec2::23]/v1/credentials", true)]
    [InlineData("http://127.0.0.1:8080/creds", true)]
    [InlineData("https://creds.example.com/creds", true)]
    [InlineData("http://creds.example.com/creds", false)]
    public async Task FullContainerUrisUseHttpOnlyForKnownHosts(string uri, bool allowed)
    {
        using var httpClient = new HttpClient(
            new Handler(_ => Json("""{"AccessKeyId":"AKID","SecretAccessKey":"secret"}"""))
        );
        var provider = new ContainerAwsCredentialsProvider(
            httpClient,
            Environment(("AWS_CONTAINER_CREDENTIALS_FULL_URI", uri))
        );

        if (allowed)
        {
            Assert.Equal("AKID", (await provider.GetCredentialsAsync()).AccessKeyId);
        }
        else
        {
            var error = await Assert.ThrowsAsync<AwsCredentialsProviderException>(async () =>
                await provider.GetCredentialsAsync()
            );
            Assert.False(error.IsNotConfigured);
        }
    }

    [Fact]
    public async Task TheContainerTokenFileIsReadOnEveryRequest()
    {
        using var directory = new TemporaryDirectory();
        var tokenFile = directory.Write("token", "first");
        var tokens = new List<string>();
        using var httpClient = new HttpClient(
            new Handler(request =>
            {
                tokens.Add(request.Headers.GetValues("Authorization").Single());
                return Json("""{"AccessKeyId":"AKID","SecretAccessKey":"secret"}""");
            })
        );
        var provider = new ContainerAwsCredentialsProvider(
            httpClient,
            Environment(
                ("AWS_CONTAINER_CREDENTIALS_FULL_URI", "http://169.254.170.23/v1/credentials"),
                ("AWS_CONTAINER_AUTHORIZATION_TOKEN_FILE", tokenFile)
            )
        );

        await provider.GetCredentialsAsync();
        File.WriteAllText(tokenFile, "second");
        await provider.GetCredentialsAsync();

        Assert.Equal(["first", "second"], tokens);
    }

    // ---------------- process command lines ----------------

    [Theory]
    [InlineData("creds", new[] { "creds" })]
    [InlineData("  creds   --a  b ", new[] { "creds", "--a", "b" })]
    [InlineData("creds 'one two' \"three four\"", new[] { "creds", "one two", "three four" })]
    [InlineData(@"creds a\ b 'it''s'", new[] { "creds", "a b", "its" })]
    public async Task CommandLinesSplitLikeTheAwsCli(string commandLine, string[] expected)
    {
        IReadOnlyList<string>? ran = null;
        var provider = new ProcessAwsCredentialsProvider(
            commandLine,
            (arguments, _) =>
            {
                ran = arguments;
                return Task.FromResult(
                    new ProcessAwsCredentialsResult(
                        0,
                        """{"Version":1,"AccessKeyId":"AKID","SecretAccessKey":"secret"}""",
                        ""
                    )
                );
            }
        );

        await provider.GetCredentialsAsync();

        Assert.Equal(expected, ran);
    }

    // ---------------- chain ----------------

    [Fact]
    public async Task TheChainSticksToTheSourceThatSucceeded()
    {
        var unconfigured = new CountingProvider(
            new AwsCredentialsProviderException("first", "not configured", isNotConfigured: true)
        );
        var configured = new CountingProvider(new AwsCredentials("AKID", "secret"));
        var chain = new DefaultAwsCredentialsProvider([unconfigured, configured]);

        await chain.GetCredentialsAsync();
        await chain.GetCredentialsAsync();

        Assert.Equal(1, unconfigured.Calls);
        Assert.Equal(2, configured.Calls);
    }

    [Fact]
    public async Task AConfiguredSourceThatFailsStopsTheChain()
    {
        var failing = new CountingProvider(
            new AwsCredentialsProviderException("profile", "role could not be assumed")
        );
        var later = new CountingProvider(new AwsCredentials("AKID", "secret"));
        var chain = new DefaultAwsCredentialsProvider([failing, later]);

        await Assert.ThrowsAsync<AwsCredentialsProviderException>(async () =>
            await chain.GetCredentialsAsync()
        );

        Assert.Equal(0, later.Calls);
    }

    // ---------------- helpers ----------------

    private static ProfileAwsCredentialsProvider Profile(
        string name,
        string configPath,
        string credentialsPath,
        HttpClient? httpClient = null
    ) =>
        new(
            name,
            credentialsPath,
            configPath,
            httpClient: httpClient,
            timeProvider: new FixedTimeProvider(Now),
            getEnvironmentVariable: Environment()
        );

    private static Func<string, string?> Environment(params (string Name, string Value)[] values)
    {
        var map = values.ToDictionary(value => value.Name, value => value.Value);
        return name => map.GetValueOrDefault(name);
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

    /// <summary>
    /// Answers STS form requests with credentials named after the role, so tests can tell which
    /// assumed role signed the next call.
    /// </summary>
    private sealed class FakeSts : HttpMessageHandler
    {
        public bool Deny { get; init; }

        public List<StsRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            var form = body.Split('&')
                .Select(pair => pair.Split('=', 2))
                .ToDictionary(
                    pair => Uri.UnescapeDataString(pair[0]),
                    pair => Uri.UnescapeDataString(pair[1])
                );
            Requests.Add(
                new StsRequest(
                    request.RequestUri!.AbsoluteUri,
                    form,
                    request.Headers.TryGetValues("Authorization", out var values)
                        ? values.Single()
                        : null
                )
            );
            if (Deny)
            {
                return new HttpResponseMessage(HttpStatusCode.Forbidden)
                {
                    Content = new StringContent(
                        """
                        <ErrorResponse xmlns="https://sts.amazonaws.com/doc/2011-06-15/">
                          <Error><Type>Sender</Type><Code>AccessDenied</Code><Message>Not authorized to perform sts:AssumeRole</Message></Error>
                        </ErrorResponse>
                        """
                    ),
                };
            }
            var role = form["RoleArn"][(form["RoleArn"].LastIndexOf('/') + 1)..];
            var action = form["Action"];
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $"""
                    <{action}Response xmlns="https://sts.amazonaws.com/doc/2011-06-15/">
                      <{action}Result>
                        <Credentials>
                          <AccessKeyId>ASIA-{role}</AccessKeyId>
                          <SecretAccessKey>secret-{role}</SecretAccessKey>
                          <SessionToken>session-{role}</SessionToken>
                          <Expiration>2026-10-25T13:00:00Z</Expiration>
                        </Credentials>
                      </{action}Result>
                    </{action}Response>
                    """
                ),
            };
        }
    }

    private sealed record StsRequest(
        string Uri,
        Dictionary<string, string> Form,
        string? Authorization
    );

    private sealed class CountingProvider : IAwsCredentialsProvider
    {
        private readonly AwsCredentials? credentials;
        private readonly Exception? exception;

        public CountingProvider(AwsCredentials credentials) => this.credentials = credentials;

        public CountingProvider(Exception exception) => this.exception = exception;

        public int Calls { get; private set; }

        public ValueTask<AwsCredentials> GetCredentialsAsync(
            CancellationToken cancellationToken = default
        )
        {
            Calls++;
            return exception is null
                ? ValueTask.FromResult(credentials!)
                : ValueTask.FromException<AwsCredentials>(exception);
        }
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) => Task.FromResult(respond(request));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory() =>
            Path = Directory.CreateTempSubdirectory("nsmithy-aws-chain-").FullName;

        public string Path { get; }

        public string Missing => System.IO.Path.Combine(Path, "missing");

        public string Write(string name, string content)
        {
            var path = System.IO.Path.Combine(Path, name);
            File.WriteAllText(path, content);
            return path;
        }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
