using System.Collections.ObjectModel;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using NSmithy.Codecs.Json;
using NSmithy.Core;
using NSmithy.Core.Serde;
using NSmithy.Core.Validation;
using NSmithy.Server;

namespace NSmithy.Server.Mcp;

/// <summary>Creates MCP tools from a generated service operation catalog.</summary>
public static class SmithyMcpTools
{
    /// <summary>
    /// Creates one tool for each unary operation in <paramref name="catalog"/>. Operations with a
    /// streaming input or output are omitted because MCP tool calls have unary JSON arguments and
    /// results.
    /// </summary>
    public static IReadOnlyList<McpServerTool> Create(ServiceOperationCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        List<McpServerTool> tools = [];
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var operation in catalog.Operations)
        {
            SmithyMcpTool tool;
            try
            {
                tool = new SmithyMcpTool(operation);
            }
            catch (McpStreamingNotSupportedException)
            {
                continue;
            }

            if (!names.Add(tool.ProtocolTool.Name))
            {
                throw new InvalidOperationException(
                    $"Service '{catalog.Schema.Id}' contains more than one operation that maps to "
                        + $"MCP tool name '{tool.ProtocolTool.Name}'."
                );
            }

            tools.Add(tool);
        }

        return new ReadOnlyCollection<McpServerTool>(tools);
    }
}

internal sealed class SmithyMcpTool : McpServerTool
{
    private static readonly ShapeId DocumentationTrait = ShapeId.Parse("smithy.api#documentation");
    private static readonly ShapeId IdempotentTrait = ShapeId.Parse("smithy.api#idempotent");
    private static readonly ShapeId ReadonlyTrait = ShapeId.Parse("smithy.api#readonly");

    private readonly IServiceOperation operation;
    private readonly IBoxedJsonCodec inputCodec;
    private readonly IBoxedJsonCodec outputCodec;

    public SmithyMcpTool(IServiceOperation operation)
    {
        this.operation = operation ?? throw new ArgumentNullException(nameof(operation));
        var schema = operation.Schema;
        if (schema.IsStreaming)
        {
            throw new McpStreamingNotSupportedException(schema.Id);
        }

        var jsonSchemas =
            operation.JsonSchemas
            ?? throw new InvalidOperationException(
                $"Operation '{schema.Id}' does not contain generated JSON Schema metadata."
            );
        var inputSchema = ParseSchema(jsonSchemas.Input, schema.Id, requireObject: true);
        var outputSchema = ParseSchema(jsonSchemas.Output, schema.Id, requireObject: false);
        (inputCodec, outputCodec) = schema.Accept(BoxedJsonCodec.Compiler.Instance);

        var readOnly = schema.HasTrait(ReadonlyTrait);
        ProtocolTool = new Tool
        {
            Name = schema.Id.Name,
            Description = StringTrait(schema, DocumentationTrait),
            InputSchema = inputSchema,
            OutputSchema = outputSchema,
            Annotations = new ToolAnnotations
            {
                ReadOnlyHint = readOnly,
                DestructiveHint = readOnly ? false : null,
                IdempotentHint = schema.HasTrait(IdempotentTrait),
            },
        };
    }

    public override Tool ProtocolTool { get; }

    public override IReadOnlyList<object> Metadata { get; } = [];

    private static JsonElement ParseSchema(string json, ShapeId operationId, bool requireObject)
    {
        using var document = JsonDocument.Parse(json);
        var schema = document.RootElement;
        if (
            requireObject
            && (!schema.TryGetProperty("type", out var type) || type.GetString() != "object")
        )
        {
            throw new NotSupportedException(
                $"MCP tool input for operation '{operationId}' must be a JSON object schema."
            );
        }

        return schema.Clone();
    }

    public override async ValueTask<CallToolResult> InvokeAsync(
        RequestContext<CallToolRequestParams> request,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(request);

        object input;
        try
        {
            input = inputCodec.Deserialize(WriteArguments(request.Params.Arguments));
        }
        catch (MissingRequiredMemberException exception)
        {
            return Error(ValidationException.FromMissingRequiredMember(exception).Message);
        }
        catch (MalformedRequestException exception)
        {
            return Error(exception.Message);
        }

        object? output;
        try
        {
            output = await operation.InvokeAsync(input, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsModeledError(exception))
        {
            return Error(exception.Message);
        }

        var payload = outputCodec.Serialize(output);
        using var document = JsonDocument.Parse(payload);
        var structuredContent = document.RootElement.Clone();
        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = structuredContent.GetRawText() }],
            StructuredContent = structuredContent,
        };
    }

    private bool IsModeledError(Exception exception)
    {
        var matcher = new OperationErrorMatcher(exception);
        return operation.Schema.Errors.Any(error => error.Accept(matcher));
    }

    private static byte[] WriteArguments(IDictionary<string, JsonElement>? arguments)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            if (arguments is not null)
            {
                foreach (var argument in arguments)
                {
                    writer.WritePropertyName(argument.Key);
                    argument.Value.WriteTo(writer);
                }
            }

            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    private static CallToolResult Error(string message) =>
        new() { Content = [new TextContentBlock { Text = message }], IsError = true };

    private static string? StringTrait(IOperationSchema schema, ShapeId id) =>
        schema.GetTrait(id)?.Value is { Kind: DocumentKind.String } value ? value.AsString() : null;

    private sealed class OperationErrorMatcher(Exception exception)
        : IOperationErrorSchemaVisitor<bool>
    {
        public bool Visit<TError>(OperationErrorSchema<TError> schema)
            where TError : Exception => exception is TError;
    }
}

internal interface IBoxedJsonCodec
{
    object Deserialize(byte[] payload);

    byte[] Serialize(object? value);
}

internal static class BoxedJsonCodec
{
    /// <summary>Compiles the codecs for an operation's input and output.</summary>
    public sealed class Compiler
        : IOperationSchemaVisitor<(IBoxedJsonCodec Input, IBoxedJsonCodec Output)>
    {
        public static Compiler Instance { get; } = new();

        public (IBoxedJsonCodec Input, IBoxedJsonCodec Output) Visit<TInput, TOutput>(
            OperationSchema<TInput, TOutput> schema
        ) => (new JsonCodec<TInput>(schema.Input), new JsonCodec<TOutput>(schema.Output));
    }

    private sealed class JsonCodec<T>(Schema<T> schema) : IBoxedJsonCodec
    {
        private readonly ICodec<T> codec = JsonCodecFactory.Strict.FromSchema(schema);

        public object Deserialize(byte[] payload) => codec.Deserialize(payload)!;

        public byte[] Serialize(object? value)
        {
            if (value is not T && (value is not null || default(T) is not null))
            {
                throw new ArgumentException(
                    $"Expected a value assignable to '{typeof(T).FullName}'.",
                    nameof(value)
                );
            }

            return codec.Serialize((T)value!);
        }
    }
}

internal sealed class McpStreamingNotSupportedException(ShapeId id)
    : NotSupportedException($"MCP tools do not support streaming shape '{id}'.");
