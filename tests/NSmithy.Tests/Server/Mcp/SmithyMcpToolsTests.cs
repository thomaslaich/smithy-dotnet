using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using NSmithy.Core;
using NSmithy.Core.Serde;
using NSmithy.Server;
using NSmithy.Server.Mcp;

namespace NSmithy.Tests.Server.Mcp;

public sealed class SmithyMcpToolsTests
{
    private static readonly ShapeId DocumentationTrait = ShapeId.Parse("smithy.api#documentation");
    private static readonly ShapeId IdempotentTrait = ShapeId.Parse("smithy.api#idempotent");
    private static readonly ShapeId JsonNameTrait = ShapeId.Parse("smithy.api#jsonName");
    private static readonly ShapeId LengthTrait = ShapeId.Parse("smithy.api#length");
    private static readonly ShapeId ReadonlyTrait = ShapeId.Parse("smithy.api#readonly");

    [Fact]
    public void CreatesDocumentedToolSchemasAndAnnotations()
    {
        var tools = SmithyMcpTools.Create(
            Catalog(static (_, _) => Task.FromResult(new LookupOutput("sunny")))
        );

        var tool = Assert.Single(tools).ProtocolTool;
        Assert.Equal("LookupWeather", tool.Name);
        Assert.Equal("Looks up the weather for a place.", tool.Description);
        Assert.True(tool.Annotations?.ReadOnlyHint);
        Assert.False(tool.Annotations?.DestructiveHint);
        Assert.True(tool.Annotations?.IdempotentHint);

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
        LookupInput? received = null;
        using var cancellation = new CancellationTokenSource();
        var tool = Assert.Single(
            SmithyMcpTools.Create(
                Catalog(
                    (input, cancellationToken) =>
                    {
                        received = input;
                        Assert.Equal(cancellation.Token, cancellationToken);
                        return Task.FromResult(new LookupOutput($"Sunny in {input.Place}"));
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

        Assert.Equal(new LookupInput("Zurich"), received);
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
                        return Task.FromResult(new LookupOutput(input.Place));
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
        var operationSchema = Schemas.Operation(
            ShapeId.Parse("example.weather#LookupWeather"),
            LookupInputSchema,
            LookupOutputSchema,
            errors:
            [
                Schemas.OperationError(
                    ShapeId.Parse("example.weather#LookupFailure"),
                    LookupFailureSchema,
                    400
                ),
            ]
        );
        var catalog = new ServiceOperationCatalog(
            ServiceSchema,
            ServiceOperation.Create(
                operationSchema,
                static (LookupInput _, CancellationToken _) =>
                    Task.FromException<LookupOutput>(new LookupFailure("No forecast available.")),
                LookupJsonSchemas
            )
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
                ShapeId.Parse("example.weather#WatchWeather"),
                Schemas.EventStream(Schemas.String),
                LookupOutputSchema,
                isStreaming: true
            ),
            static (IAsyncEnumerable<string> _, CancellationToken _) =>
                Task.FromResult(new LookupOutput("unused"))
        );
        var catalog = new ServiceOperationCatalog(ServiceSchema, operation);

        Assert.Empty(SmithyMcpTools.Create(catalog));
    }

    [Fact]
    public void RequiresJsonSchemaMetadataForHandBuiltOperations()
    {
        var operation = ServiceOperation.Create(
            Schemas.Operation(
                ShapeId.Parse("example.weather#LookupWeather"),
                LookupInputSchema,
                LookupOutputSchema
            ),
            static (LookupInput _, CancellationToken _) =>
                Task.FromResult(new LookupOutput("unused"))
        );
        var catalog = new ServiceOperationCatalog(ServiceSchema, operation);

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
            .WithSmithyTools(Catalog(static (_, _) => Task.FromResult(new LookupOutput("sunny"))));

        using var provider = services.BuildServiceProvider();
        Assert.Single(provider.GetServices<McpServerTool>());
    }

    [Fact]
    public void ResolvesGeneratedServiceDefinitionWithoutAnAggregateHandler()
    {
        var services = new ServiceCollection();
        services.AddSingleton<LookupHandler>();
        services.AddSingleton<IServiceDefinition, LookupServiceDefinition>();
        services.AddMcpServer().WithSmithyService(ServiceSchema);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<McpServerOptions>>().Value;

        Assert.Single(options.ToolCollection!);
        Assert.Single(options.PromptCollection!);
    }

    private static ServiceOperationCatalog Catalog(
        Func<LookupInput, CancellationToken, Task<LookupOutput>> handler
    ) =>
        new(
            ServiceSchema,
            ServiceOperation.Create(
                Schemas.Operation(
                    ShapeId.Parse("example.weather#LookupWeather"),
                    LookupInputSchema,
                    LookupOutputSchema,
                    traits:
                    [
                        new Trait(
                            DocumentationTrait,
                            Document.From("Looks up the weather for a place.")
                        ),
                        new Trait(IdempotentTrait),
                        new Trait(ReadonlyTrait),
                    ]
                ),
                handler,
                LookupJsonSchemas
            )
        );

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

    private static readonly ServiceSchema ServiceSchema = Schemas.Service(
        ShapeId.Parse("example.weather#WeatherService")
    );

    private static readonly OperationJsonSchemas LookupJsonSchemas = new(
        """
        {"$schema":"https://json-schema.org/draft/2020-12/schema","type":"object","properties":{"place_name":{"type":"string","description":"Place to look up.","minLength":2,"maxLength":80}},"required":["place_name"],"additionalProperties":false}
        """,
        """
        {"$schema":"https://json-schema.org/draft/2020-12/schema","type":"object","properties":{"summary":{"type":"string"}},"required":["summary"],"additionalProperties":false}
        """
    );

    private static readonly Schema<LookupInput> LookupInputSchema = Schemas
        .Structure<LookupInput, LookupInputBuilder>(ShapeId.Parse("example.weather#LookupInput"))
        .Required(
            "place",
            static value => value.Place,
            static (builder, value) => builder.Place = value,
            Schemas.String,
            [
                new Trait(DocumentationTrait, Document.From("Place to look up.")),
                new Trait(JsonNameTrait, Document.From("place_name")),
                new Trait(
                    LengthTrait,
                    Document.From(
                        new Dictionary<string, Document>
                        {
                            ["min"] = Document.From(2m),
                            ["max"] = Document.From(80m),
                        }
                    )
                ),
            ]
        )
        .Build(
            static () => new LookupInputBuilder(),
            static builder => new LookupInput(
                builder.Place ?? throw new MissingRequiredMemberException("place")
            )
        );

    private static readonly Schema<LookupOutput> LookupOutputSchema = Schemas
        .Structure<LookupOutput, LookupOutputBuilder>(ShapeId.Parse("example.weather#LookupOutput"))
        .Required(
            "summary",
            static value => value.Summary,
            static (builder, value) => builder.Summary = value,
            Schemas.String
        )
        .Build(
            static () => new LookupOutputBuilder(),
            static builder => new LookupOutput(
                builder.Summary ?? throw new MissingRequiredMemberException("summary")
            )
        );

    private static readonly Schema<LookupFailure> LookupFailureSchema = Schemas
        .Structure<LookupFailure, LookupFailureBuilder>(
            ShapeId.Parse("example.weather#LookupFailure")
        )
        .Required(
            "message",
            static value => value.Message,
            static (builder, value) => builder.Message = value,
            Schemas.String
        )
        .Build(
            static () => new LookupFailureBuilder(),
            static builder => new LookupFailure(
                builder.Message ?? throw new MissingRequiredMemberException("message")
            )
        );

    private sealed record LookupInput(string Place);

    private sealed class LookupInputBuilder
    {
        public string? Place { get; set; }
    }

    private sealed record LookupOutput(string Summary);

    private sealed class LookupOutputBuilder
    {
        public string? Summary { get; set; }
    }

    private sealed class LookupFailure(string message) : Exception(message);

    private sealed class LookupFailureBuilder
    {
        public string? Message { get; set; }
    }

    private sealed class LookupHandler
    {
        private readonly string suffix = string.Empty;

        public Task<LookupOutput> InvokeAsync(
            LookupInput input,
            CancellationToken cancellationToken
        ) => Task.FromResult(new LookupOutput(input.Place + suffix));
    }

    private sealed class LookupServiceDefinition : IServiceDefinition
    {
        public ServiceSchema Schema => ServiceSchema;

        public IReadOnlyList<ServicePromptDefinition> Prompts { get; } =
        [new("weather_brief", "Create a weather brief", "Call LookupWeather.")];

        public ServiceOperationCatalog CreateOperationCatalog(IServiceProvider services) =>
            Catalog(services.GetRequiredService<LookupHandler>().InvokeAsync);
    }
}
