using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using NSmithy.Core;
using NSmithy.Core.Serde;
using NSmithy.Server;
using NSmithy.Server.Mcp;
using Nsmithy.Tests.Mcp;

namespace NSmithy.Tests.Server.Mcp;

public sealed class SmithyMcpToolsTests
{
    [Fact]
    public void CreatesDocumentedToolSchemasAndAnnotations()
    {
        var tools = SmithyMcpTools.Create(
            Catalog(static (_, _) => Task.FromResult(new LookupWeatherOutput("sunny")))
        );

        var tool = Assert.Single(tools).ProtocolTool;
        Assert.Equal("LookupWeather", tool.Name);
        Assert.Equal("Looks up the weather for a place.", tool.Description);
        Assert.True(tool.Annotations?.ReadOnlyHint);
        Assert.False(tool.Annotations?.DestructiveHint);

        var input = tool.InputSchema;
        Assert.Equal("object", input.GetProperty("type").GetString());
        Assert.False(input.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal("place_name", input.GetProperty("required")[0].GetString());
        var place = input.GetProperty("properties").GetProperty("place_name");
        Assert.Equal("string", place.GetProperty("type").GetString());
        Assert.Equal("Place to look up.", place.GetProperty("description").GetString());
        Assert.Equal(2, place.GetProperty("minLength").GetInt32());
        Assert.Equal(80, place.GetProperty("maxLength").GetInt32());

        var output = tool.OutputSchema!.Value;
        Assert.Equal("object", output.GetProperty("type").GetString());
        Assert.Equal(
            "string",
            output.GetProperty("properties").GetProperty("summary").GetProperty("type").GetString()
        );
    }

    [Fact]
    public async Task InvokesThroughSmithyJsonCodecAndReturnsStructuredContent()
    {
        LookupWeatherInput? received = null;
        using var cancellation = new CancellationTokenSource();
        var tool = Assert.Single(
            SmithyMcpTools.Create(
                Catalog(
                    (input, cancellationToken) =>
                    {
                        received = input;
                        Assert.Equal(cancellation.Token, cancellationToken);
                        return Task.FromResult(new LookupWeatherOutput($"Sunny in {input.Place}"));
                    }
                )
            )
        );

        var result = await InvokeAsync(
            tool,
            new Dictionary<string, JsonElement>
            {
                ["place_name"] = JsonSerializer.SerializeToElement("Zurich"),
            },
            cancellation.Token
        );

        Assert.Equal(new LookupWeatherInput("Zurich"), received);
        Assert.NotEqual(true, result.IsError);
        Assert.Equal(
            "Sunny in Zurich",
            result.StructuredContent!.Value.GetProperty("summary").GetString()
        );
        Assert.Equal(
            "{\"summary\":\"Sunny in Zurich\"}",
            Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text
        );
    }

    [Fact]
    public async Task ReportsMissingAndInvalidArgumentsAsToolErrors()
    {
        var invocationCount = 0;
        var tool = Assert.Single(
            SmithyMcpTools.Create(
                Catalog(
                    (input, _) =>
                    {
                        invocationCount++;
                        return Task.FromResult(new LookupWeatherOutput(input.Place));
                    }
                )
            )
        );

        var missing = await InvokeAsync(tool, null);
        var invalid = await InvokeAsync(
            tool,
            new Dictionary<string, JsonElement>
            {
                ["place_name"] = JsonSerializer.SerializeToElement("Z"),
            }
        );

        Assert.True(missing.IsError);
        Assert.Contains(
            "/place",
            Assert.IsType<TextContentBlock>(Assert.Single(missing.Content)).Text,
            StringComparison.Ordinal
        );
        Assert.True(invalid.IsError);
        Assert.Contains(
            "length",
            Assert.IsType<TextContentBlock>(Assert.Single(invalid.Content)).Text,
            StringComparison.OrdinalIgnoreCase
        );
        Assert.Equal(0, invocationCount);
    }

    [Fact]
    public async Task ReportsModeledHandlerErrorsAsToolErrors()
    {
        var catalog = Catalog(
            static (_, _) =>
                Task.FromException<LookupWeatherOutput>(new LookupFailure("No forecast available."))
        );
        var tool = Assert.Single(SmithyMcpTools.Create(catalog));

        var result = await InvokeAsync(
            tool,
            new Dictionary<string, JsonElement>
            {
                ["place_name"] = JsonSerializer.SerializeToElement("Zurich"),
            }
        );

        Assert.True(result.IsError);
        Assert.Equal(
            "No forecast available.",
            Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text
        );
    }

    [Fact]
    public void OmitsStreamingOperations()
    {
        var operation = ServiceOperation.Create(
            Schemas.Operation(
                ShapeId.Parse("nsmithy.tests.mcp#WatchWeather"),
                Schemas.EventStream(Schemas.String),
                LookupWeatherOutputSchema.Schema,
                isStreaming: true
            ),
            static (IAsyncEnumerable<string> _, CancellationToken _) =>
                Task.FromResult(new LookupWeatherOutput("unused"))
        );
        var catalog = new ServiceOperationCatalog(FixturesSchema.Schema, operation);

        Assert.Empty(SmithyMcpTools.Create(catalog));
    }

    [Fact]
    public void RequiresJsonSchemaMetadataForHandBuiltOperations()
    {
        var operation = ServiceOperation.Create(
            LookupWeatherSchema.Schema,
            static (LookupWeatherInput _, CancellationToken _) =>
                Task.FromResult(new LookupWeatherOutput("unused"))
        );
        var catalog = new ServiceOperationCatalog(FixturesSchema.Schema, operation);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            SmithyMcpTools.Create(catalog)
        );

        Assert.Contains("JSON Schema metadata", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RegistersToolsWithTheOfficialMcpBuilder()
    {
        var services = new ServiceCollection();
        services
            .AddMcpServer()
            .WithSmithyTools(
                Catalog(static (_, _) => Task.FromResult(new LookupWeatherOutput("sunny")))
            );

        using var provider = services.BuildServiceProvider();
        Assert.Single(provider.GetServices<McpServerTool>());
    }

    [Fact]
    public void ResolvesGeneratedServiceDefinitionWithoutAnAggregateHandler()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILookupWeatherHandler>(
            new LookupHandler(
                static (input, _) => Task.FromResult(new LookupWeatherOutput(input.Place))
            )
        );
        services.AddFixturesService();
        services.AddMcpServer().WithSmithyService(FixturesSchema.Schema);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<McpServerOptions>>().Value;

        Assert.Single(options.ToolCollection!);
        Assert.Single(options.PromptCollection!);
    }

    private static ServiceOperationCatalog Catalog(
        Func<LookupWeatherInput, CancellationToken, Task<LookupWeatherOutput>> handler
    )
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILookupWeatherHandler>(new LookupHandler(handler));
        using var provider = services.BuildServiceProvider();
        return new FixturesServiceDefinition().CreateOperationCatalog(provider);
    }

    private static async Task<CallToolResult> InvokeAsync(
        McpServerTool tool,
        IDictionary<string, JsonElement>? arguments,
        CancellationToken cancellationToken = default
    )
    {
        await using var server = McpServer.Create(
            new StreamServerTransport(new MemoryStream(), new MemoryStream()),
            new McpServerOptions()
        );
        var context = new RequestContext<CallToolRequestParams>(
            server,
            new JsonRpcRequest { Id = new RequestId(1), Method = RequestMethods.ToolsCall },
            new CallToolRequestParams { Name = tool.ProtocolTool.Name, Arguments = arguments }
        );
        return await tool.InvokeAsync(context, cancellationToken);
    }

    private sealed class LookupHandler(
        Func<LookupWeatherInput, CancellationToken, Task<LookupWeatherOutput>> handler
    ) : ILookupWeatherHandler
    {
        public Task<LookupWeatherOutput> LookupWeatherAsync(
            LookupWeatherInput input,
            CancellationToken cancellationToken = default
        ) => handler(input, cancellationToken);
    }
}
