using System.Net.Http.Headers;
using Bench.Corpus;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bench.Hosting;

/// <summary>
/// One stack, hosted in-process over <see cref="TestServer"/>.
/// </summary>
public sealed class BenchServer : IAsyncDisposable
{
    private readonly WebApplication app;

    private BenchServer(string name, WebApplication app, HttpClient client)
    {
        Name = name;
        this.app = app;
        Client = client;
    }

    /// <summary>Stack name, used as a BenchmarkDotNet parameter and in parity output.</summary>
    public string Name { get; }

    /// <summary>An <see cref="HttpClient"/> bound to this server's in-memory transport.</summary>
    public HttpClient Client { get; }

    /// <summary>
    /// A fresh handler onto this server's in-memory transport, for callers that
    /// need to wrap or compose it, the parity gate layers a recording
    /// handler over this one.
    /// </summary>
    public HttpMessageHandler CreateHandler() => app.GetTestServer().CreateHandler();

    /// <summary>Starts a stack given its endpoint/service registration.</summary>
    public static async Task<BenchServer> StartAsync(
        string name,
        Action<WebApplicationBuilder> configureServices,
        Action<WebApplication> configureApp
    )
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();

        // Logging is pure overhead here and its cost differs by stack, which
        // would show up as a framework difference that is not one.
        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.None);

        configureServices(builder);

        var app = builder.Build();
        configureApp(app);
        await app.StartAsync();

        var client = app.GetTestClient();
        client.BaseAddress = new Uri("http://localhost/");
        return new BenchServer(name, app, client);
    }

    /// <summary>Builds a live <see cref="HttpRequestMessage"/> for a corpus scenario.</summary>
    public static HttpRequestMessage BuildRequest(BenchRequest request)
    {
        var message = new HttpRequestMessage(new HttpMethod(request.Method), request.PathAndQuery);

        foreach (var (headerName, headerValue) in request.Headers)
        {
            if (headerName.Equals("content-type", StringComparison.OrdinalIgnoreCase))
                continue;

            message.Headers.TryAddWithoutValidation(headerName, headerValue);
        }

        if (request.Body is { } body)
        {
            var content = new ByteArrayContent(body);
            var contentType = request
                .Headers.FirstOrDefault(h =>
                    h.Name.Equals("content-type", StringComparison.OrdinalIgnoreCase)
                )
                .Value;
            content.Headers.ContentType = MediaTypeHeaderValue.Parse(
                contentType ?? "application/json"
            );
            message.Content = content;
        }

        return message;
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await app.StopAsync();
        await app.DisposeAsync();
    }
}
