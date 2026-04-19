using System.Globalization;
using System.Text;
using SmithyNet.CodeGeneration.Model;
using SmithyNet.Core;
using SmithyNet.Core.Traits;

namespace SmithyNet.CodeGeneration.CSharp;

#pragma warning disable CA1305 // Source text is assembled from Smithy identifiers and explicitly formatted literals.

public sealed class CSharpShapeGenerator
{
    public IReadOnlyList<GeneratedCSharpFile> Generate(
        SmithyModel model,
        CSharpGenerationOptions? options = null
    )
    {
        ArgumentNullException.ThrowIfNull(model);
        options ??= new CSharpGenerationOptions();

        return
        [
            .. model
                .Shapes.Values.Where(shape => ShouldGenerate(shape, options))
                .OrderBy(shape => shape.Id.ToString(), StringComparer.Ordinal)
                .Select(shape => GenerateShape(model, shape, options)),
            .. model
                .Shapes.Values.Where(shape => ShouldGenerateClient(shape, options))
                .OrderBy(shape => shape.Id.ToString(), StringComparer.Ordinal)
                .Select(shape => GenerateClient(model, shape, options)),
        ];
    }

    private static bool ShouldGenerate(ModelShape shape, CSharpGenerationOptions options)
    {
        return ShouldGenerateNamespace(shape, options)
            && shape.Kind
                is ShapeKind.Structure
                    or ShapeKind.List
                    or ShapeKind.Set
                    or ShapeKind.Map
                    or ShapeKind.Enum
                    or ShapeKind.IntEnum
                    or ShapeKind.Union;
    }

    private static bool ShouldGenerateClient(ModelShape shape, CSharpGenerationOptions options)
    {
        return ShouldGenerateNamespace(shape, options)
            && shape.Kind == ShapeKind.Service
            && shape.Traits.Has(SmithyPrelude.RestJson1Trait);
    }

    private static bool ShouldGenerateNamespace(ModelShape shape, CSharpGenerationOptions options)
    {
        return options.GeneratedNamespaces is not { Count: > 0 } generatedNamespaces
            || generatedNamespaces.Contains(shape.Id.Namespace, StringComparer.Ordinal);
    }

    private static GeneratedCSharpFile GenerateShape(
        SmithyModel model,
        ModelShape shape,
        CSharpGenerationOptions options
    )
    {
        var contents = shape.Kind switch
        {
            ShapeKind.Structure when shape.Traits.Has(SmithyPrelude.ErrorTrait) => GenerateError(
                model,
                shape,
                options
            ),
            ShapeKind.Structure => GenerateStructure(model, shape, options),
            ShapeKind.List or ShapeKind.Set => GenerateList(model, shape, options),
            ShapeKind.Map => GenerateMap(model, shape, options),
            ShapeKind.Enum => GenerateStringEnum(shape, options),
            ShapeKind.IntEnum => GenerateIntEnum(shape, options),
            ShapeKind.Union => GenerateUnion(model, shape, options),
            _ => throw new InvalidOperationException(
                $"Shape kind '{shape.Kind}' is not supported by the C# shape generator."
            ),
        };

        return new GeneratedCSharpFile(GetPath(shape), contents);
    }

    private static GeneratedCSharpFile GenerateClient(
        SmithyModel model,
        ModelShape service,
        CSharpGenerationOptions options
    )
    {
        var builder = CreateFileBuilder(
            service,
            options,
            [
                "System.Collections",
                "System.Globalization",
                "System.Net.Http",
                "System.Text.Json",
                "System.Text",
                "System.Threading",
                "System.Threading.Tasks",
                "SmithyNet.Client",
                "SmithyNet.Http",
                "SmithyNet.Json",
            ]
        );
        var typeName = $"{GetTypeName(service.Id)}Client";
        var interfaceName = $"I{typeName}";
        builder.Line($"public interface {interfaceName}");
        builder.Block(() =>
        {
            foreach (
                var operationId in service.Operations.OrderBy(
                    id => id.ToString(),
                    StringComparer.Ordinal
                )
            )
            {
                var operation = model.GetShape(operationId);
                builder.Line($"{GetOperationSignature(model, service, operation, options)};");
            }
        });
        builder.Line();
        builder.Line($"public sealed class {typeName} : {interfaceName}");
        builder.Block(() =>
        {
            builder.Line("private readonly SmithyOperationInvoker invoker;");
            builder.Line();
            builder.Line($"public {typeName}(Uri endpoint)");
            builder.Indented(() =>
            {
                builder.Line(
                    ": this(new HttpClient(), new SmithyClientOptions { Endpoint = endpoint })"
                );
            });
            builder.Block(() => { });
            builder.Line();
            builder.Line($"public {typeName}(HttpClient httpClient)");
            builder.Indented(() =>
            {
                builder.Line(": this(httpClient, SmithyClientOptions.Default)");
            });
            builder.Block(() => { });
            builder.Line();
            builder.Line($"public {typeName}(HttpClient httpClient, SmithyClientOptions options)");
            builder.Indented(() =>
            {
                builder.Line(
                    ": this(new SmithyOperationInvoker(new HttpClientTransport(httpClient, (options ?? throw new ArgumentNullException(nameof(options))).Endpoint), options.Middleware))"
                );
            });
            builder.Block(() => { });
            builder.Line();
            builder.Line($"public {typeName}(SmithyOperationInvoker invoker)");
            builder.Block(() =>
            {
                builder.Line(
                    "this.invoker = invoker ?? throw new ArgumentNullException(nameof(invoker));"
                );
            });
            builder.Line();

            foreach (
                var operationId in service.Operations.OrderBy(
                    id => id.ToString(),
                    StringComparer.Ordinal
                )
            )
            {
                AppendOperationMethod(builder, model, service, operationId, options);
            }

            foreach (
                var operationId in service.Operations.OrderBy(
                    id => id.ToString(),
                    StringComparer.Ordinal
                )
            )
            {
                AppendErrorDeserializer(builder, model, operationId, options);
            }

            AppendClientHelpers(builder);
        });

        return new GeneratedCSharpFile(GetClientPath(service), builder.ToString());
    }

    private static void AppendOperationMethod(
        CSharpWriter builder,
        SmithyModel model,
        ModelShape service,
        ShapeId operationId,
        CSharpGenerationOptions options
    )
    {
        var operation = model.GetShape(operationId);
        var httpBinding = ReadHttpBinding(operation);
        var inputShape = operation.Input is { } operationInput
            ? model.GetShape(operationInput)
            : null;
        var outputShape = operation.Output is { } operationOutput
            ? model.GetShape(operationOutput)
            : null;
        var outputType = operation.Output is { } output
            ? GetTypeReference(output, service.Id.Namespace, options.BaseNamespace)
            : null;

        builder.Line($"public async {GetOperationSignature(model, service, operation, options)}");
        builder.Block(() =>
        {
            if (operation.Input is not null)
            {
                builder.Line("ArgumentNullException.ThrowIfNull(input);");
            }

            AppendRequestUriBuilder(builder, inputShape, httpBinding);
            builder.Line(
                $"var request = new SmithyHttpRequest(new HttpMethod({FormatString(httpBinding.Method)}), requestUri);"
            );
            if (inputShape is not null)
            {
                AppendRequestHeaders(builder, inputShape);
            }

            if (inputShape is not null && TryGetPayloadMember(inputShape, out var payloadMember))
            {
                var propertyName = CSharpIdentifier.PropertyName(payloadMember.Name);
                builder.Line(
                    $"request.Content = SmithyJsonSerializer.Serialize(input.{propertyName});"
                );
                builder.Line("request.ContentType = \"application/json\";");
            }
            else if (inputShape is not null && HasHttpBody(inputShape))
            {
                AppendRequestBody(builder, inputShape);
            }

            builder.Line();
            builder.Line(
                $"var response = await invoker.InvokeAsync({FormatString(service.Id.Name)}, {FormatString(operation.Id.Name)}, request, Deserialize{CSharpIdentifier.TypeName(operation.Id.Name)}ErrorAsync, cancellationToken).ConfigureAwait(false);"
            );

            if (outputType is null)
            {
                builder.Line("return;");
                return;
            }

            builder.Line();
            AppendResponseReturn(
                builder,
                model,
                outputShape!,
                outputType,
                service.Id.Namespace,
                options
            );
        });
        builder.Line();
    }

    private static void AppendResponseReturn(
        CSharpWriter builder,
        SmithyModel model,
        ModelShape output,
        string outputType,
        string currentNamespace,
        CSharpGenerationOptions options
    )
    {
        if (!HasResponseBindings(output))
        {
            builder.Line(
                $"return SmithyJsonSerializer.Deserialize<{outputType}>(response.Content);"
            );
            return;
        }

        builder.Line($"return new {outputType}(");
        builder.Indented(() =>
        {
            var members = GetSortedMembers(output).ToArray();
            for (var i = 0; i < members.Length; i++)
            {
                var suffix = i == members.Length - 1 ? string.Empty : ",";
                builder.Line(
                    $"{GetResponseMemberExpression(model, output, members[i], currentNamespace, options)}{suffix}"
                );
            }
        });
        builder.Line(");");
    }

    private static string GetResponseMemberExpression(
        SmithyModel model,
        ModelShape output,
        MemberShape member,
        string currentNamespace,
        CSharpGenerationOptions options
    )
    {
        var memberType = GetMemberParameterType(model, output, member, currentNamespace, options);
        if (IsHttpHeaderMember(member))
        {
            var headerName = member
                .Traits.GetValueOrDefault(SmithyPrelude.HttpHeaderTrait)!
                .Value.AsString();
            return $"GetHeader<{memberType}>(response.Headers, {FormatString(headerName)})";
        }

        if (IsHttpResponseCodeMember(member))
        {
            return $"({memberType})(int)response.StatusCode";
        }

        if (IsHttpPayloadMember(member))
        {
            return $"DeserializeBody<{memberType}>(response.Content)";
        }

        var jsonName =
            member.Traits.GetValueOrDefault(SmithyPrelude.JsonNameTrait)?.AsString() ?? member.Name;
        return $"DeserializeBodyMember<{memberType}>(response.Content, {FormatString(jsonName)})";
    }

    private static void AppendRequestUriBuilder(
        CSharpWriter builder,
        ModelShape? input,
        HttpBinding httpBinding
    )
    {
        builder.Line(
            $"var requestUriBuilder = new StringBuilder({FormatString(httpBinding.Uri)});"
        );
        if (input is not null)
        {
            foreach (var member in GetSortedMembers(input).Where(IsHttpLabelMember))
            {
                var propertyName = CSharpIdentifier.PropertyName(member.Name);
                var labelVariableName = $"{CSharpIdentifier.ParameterName(member.Name)}Label";
                builder.Line(
                    $"var {labelVariableName} = input.{propertyName} ?? throw new ArgumentException({FormatString($"HTTP label '{member.Name}' is required.")}, nameof(input));"
                );
                builder.Line(
                    $"requestUriBuilder.Replace({FormatString($"{{{member.Name}}}")}, Uri.EscapeDataString(FormatHttpValue({labelVariableName})));"
                );
            }

            foreach (var member in GetSortedMembers(input).Where(IsHttpQueryMember))
            {
                var queryName = member
                    .Traits.GetValueOrDefault(SmithyPrelude.HttpQueryTrait)!
                    .Value.AsString();
                var propertyName = CSharpIdentifier.PropertyName(member.Name);
                builder.Line(
                    $"AppendQuery(requestUriBuilder, {FormatString(queryName)}, input.{propertyName});"
                );
            }
        }

        builder.Line("var requestUri = requestUriBuilder.ToString();");
    }

    private static void AppendRequestHeaders(CSharpWriter builder, ModelShape input)
    {
        foreach (var member in GetSortedMembers(input).Where(IsHttpHeaderMember))
        {
            var headerName = member
                .Traits.GetValueOrDefault(SmithyPrelude.HttpHeaderTrait)!
                .Value.AsString();
            var propertyName = CSharpIdentifier.PropertyName(member.Name);
            builder.Line(
                $"AddHeader(request.Headers, {FormatString(headerName)}, input.{propertyName});"
            );
        }
    }

    private static void AppendRequestBody(CSharpWriter builder, ModelShape input)
    {
        var bodyMembers = GetSortedMembers(input).Where(IsHttpBodyMember).ToArray();
        if (bodyMembers.Length == 0)
        {
            return;
        }

        builder.Line("var requestBody = new Dictionary<string, object?>");
        builder.Block(
            () =>
            {
                foreach (var member in bodyMembers)
                {
                    var propertyName = CSharpIdentifier.PropertyName(member.Name);
                    var jsonName =
                        member.Traits.GetValueOrDefault(SmithyPrelude.JsonNameTrait)?.AsString()
                        ?? member.Name;
                    builder.Line($"[{FormatString(jsonName)}] = input.{propertyName},");
                }
            },
            closingSuffix: ";"
        );
        builder.Line("request.Content = SmithyJsonSerializer.Serialize(requestBody);");
        builder.Line("request.ContentType = \"application/json\";");
    }

    private static bool IsHttpBodyMember(MemberShape member)
    {
        return !IsHttpLabelMember(member)
            && !IsHttpQueryMember(member)
            && !IsHttpHeaderMember(member)
            && !IsHttpPayloadMember(member);
    }

    private static bool IsHttpHeaderMember(MemberShape member)
    {
        return member.Traits.Has(SmithyPrelude.HttpHeaderTrait);
    }

    private static bool IsHttpLabelMember(MemberShape member)
    {
        return member.Traits.Has(SmithyPrelude.HttpLabelTrait);
    }

    private static bool IsHttpPayloadMember(MemberShape member)
    {
        return member.Traits.Has(SmithyPrelude.HttpPayloadTrait);
    }

    private static bool IsHttpQueryMember(MemberShape member)
    {
        return member.Traits.Has(SmithyPrelude.HttpQueryTrait);
    }

    private static bool IsHttpResponseCodeMember(MemberShape member)
    {
        return member.Traits.Has(SmithyPrelude.HttpResponseCodeTrait);
    }

    private static bool HasResponseBindings(ModelShape output)
    {
        return output.Members.Values.Any(member =>
            IsHttpHeaderMember(member)
            || IsHttpPayloadMember(member)
            || IsHttpResponseCodeMember(member)
        );
    }

    private static bool TryGetPayloadMember(ModelShape input, out MemberShape payloadMember)
    {
        var payloadMembers = input.Members.Values.Where(IsHttpPayloadMember).ToArray();
        if (payloadMembers.Length > 1)
        {
            throw new SmithyException(
                $"Input shape '{input.Id}' has multiple @httpPayload members."
            );
        }

        payloadMember = payloadMembers.FirstOrDefault()!;
        return payloadMember is not null;
    }

    private static string GetOperationSignature(
        SmithyModel model,
        ModelShape service,
        ModelShape operation,
        CSharpGenerationOptions options
    )
    {
        var methodName = $"{CSharpIdentifier.PropertyName(operation.Id.Name)}Async";
        var inputType = operation.Input is { } input
            ? GetTypeReference(input, service.Id.Namespace, options.BaseNamespace)
            : null;
        var outputType = operation.Output is { } output
            ? GetTypeReference(output, service.Id.Namespace, options.BaseNamespace)
            : null;
        var returnType = outputType is null ? "Task" : $"Task<{outputType}>";
        var parameters = inputType is null
            ? "CancellationToken cancellationToken = default"
            : $"{inputType} input, CancellationToken cancellationToken = default";
        return $"{returnType} {methodName}({parameters})";
    }

    private static void AppendErrorDeserializer(
        CSharpWriter builder,
        SmithyModel model,
        ShapeId operationId,
        CSharpGenerationOptions options
    )
    {
        var operation = model.GetShape(operationId);
        var methodName = $"Deserialize{CSharpIdentifier.TypeName(operation.Id.Name)}ErrorAsync";
        builder.Line(
            $"private static ValueTask<Exception?> {methodName}(SmithyHttpResponse response, CancellationToken cancellationToken)"
        );
        builder.Block(() =>
        {
            builder.Line("if (string.IsNullOrWhiteSpace(response.Content))");
            builder.Block(() =>
            {
                builder.Line("return ValueTask.FromResult<Exception?>(null);");
            });

            if (operation.Errors.Count == 0)
            {
                builder.Line();
                builder.Line("return ValueTask.FromResult<Exception?>(null);");
                return;
            }

            foreach (
                var errorId in operation.Errors.OrderBy(id => id.ToString(), StringComparer.Ordinal)
            )
            {
                var error = model.GetShape(errorId);
                if (GetHttpErrorCode(error) is not { } statusCode)
                {
                    continue;
                }

                var errorType = GetTypeReference(
                    errorId,
                    operation.Id.Namespace,
                    options.BaseNamespace
                );
                builder.Line();
                builder.Line(
                    $"if ((int)response.StatusCode == {statusCode.ToString(CultureInfo.InvariantCulture)})"
                );
                builder.Block(() =>
                {
                    builder.Line(
                        $"return ValueTask.FromResult<Exception?>({GetErrorConstructionExpression(model, error, errorId, operation.Id.Namespace, options)});"
                    );
                });
            }

            var fallbackErrorId = operation
                .Errors.OrderBy(id => id.ToString(), StringComparer.Ordinal)
                .First();
            var fallbackError = model.GetShape(fallbackErrorId);
            builder.Line();
            builder.Line(
                $"return ValueTask.FromResult<Exception?>({GetErrorConstructionExpression(model, fallbackError, fallbackErrorId, operation.Id.Namespace, options)});"
            );
        });
        builder.Line();
    }

    private static string GetErrorConstructionExpression(
        SmithyModel model,
        ModelShape error,
        ShapeId errorId,
        string currentNamespace,
        CSharpGenerationOptions options
    )
    {
        var errorType = GetTypeReference(errorId, currentNamespace, options.BaseNamespace);
        var messageMember = GetErrorMessageMember(error);
        var arguments = new List<string>
        {
            messageMember is null
                ? "null"
                : GetResponseMemberExpression(
                    model,
                    error,
                    messageMember,
                    currentNamespace,
                    options
                ),
        };
        arguments.AddRange(
            GetSortedMembers(error, messageMember)
                .Select(member =>
                    GetResponseMemberExpression(model, error, member, currentNamespace, options)
                )
        );
        return $"new {errorType}({string.Join(", ", arguments)})";
    }

    private static int? GetHttpErrorCode(ModelShape error)
    {
        return error.Traits.GetValueOrDefault(SmithyPrelude.HttpErrorTrait) is { } value
            ? (int)value.AsNumber()
            : null;
    }

    private static bool HasHttpBody(ModelShape input)
    {
        return input.Members.Values.Any(IsHttpBodyMember);
    }

    private static void AppendClientHelpers(CSharpWriter builder)
    {
        builder.Line(
            "private static void AddHeader(IDictionary<string, IReadOnlyList<string>> headers, string name, object? value)"
        );
        builder.Block(() =>
        {
            builder.Line("if (value is null)");
            builder.Block(() =>
            {
                builder.Line("return;");
            });
            builder.Line();
            builder.Line("headers[name] = [FormatHttpValue(value)];");
        });
        builder.Line();

        builder.Line(
            "private static void AppendQuery(StringBuilder builder, string name, object? value)"
        );
        builder.Block(() =>
        {
            builder.Line("if (value is null)");
            builder.Block(() =>
            {
                builder.Line("return;");
            });
            builder.Line();
            builder.Line("if (value is IEnumerable values && value is not string)");
            builder.Block(() =>
            {
                builder.Line("foreach (var item in values)");
                builder.Block(() =>
                {
                    builder.Line("AppendQueryValue(builder, name, item);");
                });
                builder.Line();
                builder.Line("return;");
            });
            builder.Line();
            builder.Line("AppendQueryValue(builder, name, value);");
        });
        builder.Line();

        builder.Line(
            "private static void AppendQueryValue(StringBuilder builder, string name, object? value)"
        );
        builder.Block(() =>
        {
            builder.Line("if (value is null)");
            builder.Block(() =>
            {
                builder.Line("return;");
            });
            builder.Line();
            builder.Line("builder.Append(builder.ToString().Contains('?') ? '&' : '?');");
            builder.Line("builder.Append(Uri.EscapeDataString(name));");
            builder.Line("builder.Append('=');");
            builder.Line("builder.Append(Uri.EscapeDataString(FormatHttpValue(value)));");
        });
        builder.Line();

        builder.Line("private static string FormatHttpValue(object value)");
        builder.Block(() =>
        {
            builder.Line("return value switch");
            builder.Block(
                () =>
                {
                    builder.Line(
                        "DateTimeOffset timestamp => timestamp.ToUniversalTime().ToString(\"O\", CultureInfo.InvariantCulture),"
                    );
                    builder.Line(
                        "IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,"
                    );
                    builder.Line("_ => value.ToString() ?? string.Empty,");
                },
                closingSuffix: ";"
            );
        });
        builder.Line();

        builder.Line("private static T DeserializeBody<T>(string content)");
        builder.Block(() =>
        {
            builder.Line("return string.IsNullOrWhiteSpace(content)");
            builder.Indented(() =>
            {
                builder.Line("? default!");
                builder.Line(": SmithyJsonSerializer.Deserialize<T>(content);");
            });
        });
        builder.Line();

        builder.Line("private static T DeserializeBodyMember<T>(string content, string name)");
        builder.Block(() =>
        {
            builder.Line("if (string.IsNullOrWhiteSpace(content))");
            builder.Block(() =>
            {
                builder.Line("return default!;");
            });
            builder.Line();
            builder.Line("using var document = JsonDocument.Parse(content);");
            builder.Line("return document.RootElement.TryGetProperty(name, out var value)");
            builder.Indented(() =>
            {
                builder.Line("? SmithyJsonSerializer.Deserialize<T>(value.GetRawText())");
                builder.Line(": default!;");
            });
        });
        builder.Line();

        builder.Line(
            "private static T GetHeader<T>(IReadOnlyDictionary<string, IReadOnlyList<string>> headers, string name)"
        );
        builder.Block(() =>
        {
            builder.Line("return headers.TryGetValue(name, out var values) && values.Count > 0");
            builder.Indented(() =>
            {
                builder.Line("? ConvertHttpValue<T>(values[0])");
                builder.Line(": default!;");
            });
        });
        builder.Line();

        builder.Line("private static T ConvertHttpValue<T>(string value)");
        builder.Block(() =>
        {
            builder.Line("var targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);");
            builder.Line("if (targetType == typeof(string))");
            builder.Block(() =>
            {
                builder.Line("return (T)(object)value;");
            });
            builder.Line();
            builder.Line("if (targetType.IsEnum)");
            builder.Block(() =>
            {
                builder.Line("return (T)Enum.Parse(targetType, value, ignoreCase: false);");
            });
            builder.Line();
            builder.Line("var constructor = targetType.GetConstructor([typeof(string)]);");
            builder.Line("if (constructor is not null)");
            builder.Block(() =>
            {
                builder.Line("return (T)constructor.Invoke([value]);");
            });
            builder.Line();
            builder.Line(
                "return (T)Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);"
            );
        });
        builder.Line();
    }

    private static HttpBinding ReadHttpBinding(ModelShape operation)
    {
        if (operation.Traits.GetValueOrDefault(SmithyPrelude.HttpTrait) is not { } value)
        {
            throw new SmithyException(
                $"Operation '{operation.Id}' cannot be generated for restJson1 because it is missing the @http trait."
            );
        }

        var properties = value.AsObject();
        var method = properties.TryGetValue("method", out var methodValue)
            ? methodValue.AsString()
            : throw new SmithyException(
                $"Operation '{operation.Id}' @http trait is missing method."
            );
        var uri = properties.TryGetValue("uri", out var uriValue)
            ? uriValue.AsString()
            : throw new SmithyException($"Operation '{operation.Id}' @http trait is missing uri.");
        return new HttpBinding(method, uri);
    }

    private static string GenerateStructure(
        SmithyModel model,
        ModelShape shape,
        CSharpGenerationOptions options
    )
    {
        var builder = CreateFileBuilder(shape, options);
        var typeName = GetTypeName(shape.Id);
        AppendShapeAttributes(builder, shape);
        builder.Line($"public sealed partial record class {typeName}");
        builder.Block(() =>
        {
            AppendConstructor(builder, model, shape, typeName, options, baseCall: null);
            AppendProperties(builder, model, shape, options);
        });
        return builder.ToString();
    }

    private static string GenerateError(
        SmithyModel model,
        ModelShape shape,
        CSharpGenerationOptions options
    )
    {
        var builder = CreateFileBuilder(shape, options);
        var typeName = GetTypeName(shape.Id);
        AppendShapeAttributes(builder, shape);
        builder.Line($"public sealed partial class {typeName} : Exception");
        builder.Block(() =>
        {
            var messageMember = GetErrorMessageMember(shape);
            AppendErrorConstructor(builder, model, shape, typeName, options, messageMember);
            AppendErrorMessageProperty(builder, messageMember);
            AppendProperties(builder, model, shape, options, messageMember);
        });
        return builder.ToString();
    }

    private static string GenerateList(
        SmithyModel model,
        ModelShape shape,
        CSharpGenerationOptions options
    )
    {
        var builder = CreateFileBuilder(shape, options);
        var typeName = GetTypeName(shape.Id);
        var member = shape.Members.TryGetValue("member", out var value)
            ? value
            : throw new SmithyException($"List shape '{shape.Id}' is missing its member target.");
        var memberType = GetValueType(
            model,
            member.Target,
            nullable: shape.Traits.Has(SmithyPrelude.SparseTrait),
            currentNamespace: shape.Id.Namespace,
            baseNamespace: options.BaseNamespace
        );
        AppendShapeAttributes(builder, shape);
        builder.Line($"public sealed partial record class {typeName}");
        builder.Block(() =>
        {
            builder.Line($"public {typeName}(IEnumerable<{memberType}> values)");
            builder.Block(() =>
            {
                builder.Line("ArgumentNullException.ThrowIfNull(values);");
                builder.Line("Values = Array.AsReadOnly(values.ToArray());");
            });
            builder.Line();
            AppendMemberAttributes(
                builder,
                member,
                isSparse: shape.Traits.Has(SmithyPrelude.SparseTrait)
            );
            builder.Line($"public IReadOnlyList<{memberType}> Values {{ get; }}");
        });
        return builder.ToString();
    }

    private static string GenerateMap(
        SmithyModel model,
        ModelShape shape,
        CSharpGenerationOptions options
    )
    {
        var builder = CreateFileBuilder(shape, options);
        var typeName = GetTypeName(shape.Id);
        var key = shape.Members.TryGetValue("key", out var keyValue)
            ? keyValue
            : throw new SmithyException($"Map shape '{shape.Id}' is missing its key target.");
        var value = shape.Members.TryGetValue("value", out var mapValue)
            ? mapValue
            : throw new SmithyException($"Map shape '{shape.Id}' is missing its value target.");
        var keyType = GetValueType(
            model,
            key.Target,
            nullable: false,
            currentNamespace: shape.Id.Namespace,
            baseNamespace: options.BaseNamespace
        );
        var valueType = GetValueType(
            model,
            value.Target,
            nullable: shape.Traits.Has(SmithyPrelude.SparseTrait),
            currentNamespace: shape.Id.Namespace,
            baseNamespace: options.BaseNamespace
        );

        AppendShapeAttributes(builder, shape);
        builder.Line($"public sealed partial record class {typeName}");
        builder.Block(() =>
        {
            builder.Line($"public {typeName}(IReadOnlyDictionary<{keyType}, {valueType}> values)");
            builder.Block(() =>
            {
                builder.Line("ArgumentNullException.ThrowIfNull(values);");
                builder.Line(
                    $"Values = new System.Collections.ObjectModel.ReadOnlyDictionary<{keyType}, {valueType}>(new Dictionary<{keyType}, {valueType}>(values));"
                );
            });
            builder.Line();
            AppendMemberAttributes(
                builder,
                value,
                isSparse: shape.Traits.Has(SmithyPrelude.SparseTrait)
            );
            builder.Line($"public IReadOnlyDictionary<{keyType}, {valueType}> Values {{ get; }}");
        });
        return builder.ToString();
    }

    private static string GenerateStringEnum(ModelShape shape, CSharpGenerationOptions options)
    {
        var builder = CreateFileBuilder(shape, options);
        var typeName = GetTypeName(shape.Id);
        AppendShapeAttributes(builder, shape);
        builder.Line($"public readonly partial record struct {typeName}(string Value)");
        builder.Block(() =>
        {
            foreach (
                var member in shape.Members.Values.OrderBy(
                    member => member.Name,
                    StringComparer.Ordinal
                )
            )
            {
                var propertyName = CSharpIdentifier.PropertyName(member.Name);
                var value =
                    member.Traits.GetValueOrDefault(SmithyPrelude.EnumValueTrait)?.AsString()
                    ?? member.Name;
                builder.Line($"[SmithyEnumValue({FormatString(value)})]");
                builder.Line(
                    $"public static {typeName} {propertyName} {{ get; }} = new({FormatString(value)});"
                );
            }

            builder.Line();
            builder.Line("public override string ToString()");
            builder.Block(() =>
            {
                builder.Line("return Value;");
            });
        });
        return builder.ToString();
    }

    private static string GenerateIntEnum(ModelShape shape, CSharpGenerationOptions options)
    {
        var builder = CreateFileBuilder(shape, options);
        var typeName = GetTypeName(shape.Id);
        AppendShapeAttributes(builder, shape);
        builder.Line($"public enum {typeName}");
        builder.Block(() =>
        {
            foreach (
                var member in shape.Members.Values.OrderBy(
                    member => member.Name,
                    StringComparer.Ordinal
                )
            )
            {
                var propertyName = CSharpIdentifier.PropertyName(member.Name);
                var value = member
                    .Traits.GetValueOrDefault(SmithyPrelude.EnumValueTrait)
                    ?.AsNumber();
                var suffix = value is null
                    ? string.Empty
                    : string.Create(CultureInfo.InvariantCulture, $" = {(int)value.Value}");
                if (value is not null)
                {
                    builder.Line(
                        $"[SmithyEnumValue({FormatString(((int)value.Value).ToString(CultureInfo.InvariantCulture))})]"
                    );
                }

                builder.Line($"{propertyName}{suffix},");
            }
        });
        return builder.ToString();
    }

    private static string GenerateUnion(
        SmithyModel model,
        ModelShape shape,
        CSharpGenerationOptions options
    )
    {
        var builder = CreateFileBuilder(shape, options);
        var typeName = GetTypeName(shape.Id);
        AppendShapeAttributes(builder, shape);
        builder.Line($"public abstract partial record class {typeName}");
        var members = shape
            .Members.Values.OrderBy(member => member.Name, StringComparer.Ordinal)
            .ToArray();
        builder.Block(() =>
        {
            builder.Line($"private protected {typeName}() {{ }}");
            builder.Line();
            foreach (var member in members)
            {
                var variantName = CSharpIdentifier.TypeName(member.Name);
                var valueType = GetValueType(
                    model,
                    member.Target,
                    nullable: false,
                    currentNamespace: shape.Id.Namespace,
                    baseNamespace: options.BaseNamespace
                );
                AppendMemberAttributes(builder, member, isSparse: false);
                builder.Line($"public sealed partial record class {variantName} : {typeName}");
                builder.Block(() =>
                {
                    builder.Line($"public {variantName}({valueType} value)");
                    builder.Block(() =>
                    {
                        builder.Line(
                            $"Value = {GetUnionValueAssignment(model, member.Target, "value")};"
                        );
                    });
                    builder.Line();
                    builder.Line($"public {valueType} Value {{ get; }}");
                });
                builder.Line();
                builder.Line($"public static {typeName} From{variantName}({valueType} value)");
                builder.Block(() =>
                {
                    builder.Line($"return new {variantName}(value);");
                });
                builder.Line();
            }

            builder.Line("public sealed partial record class Unknown : " + typeName);
            builder.Block(() =>
            {
                builder.Line("public Unknown(string tag, Document value)");
                builder.Block(() =>
                {
                    builder.Line("Tag = tag ?? throw new ArgumentNullException(nameof(tag));");
                    builder.Line("Value = value;");
                });
                builder.Line();
                builder.Line("public string Tag { get; }");
                builder.Line("public Document Value { get; }");
            });
            builder.Line();
            builder.Line($"public static {typeName} FromUnknown(string tag, Document value)");
            builder.Block(() =>
            {
                builder.Line("return new Unknown(tag, value);");
            });
            builder.Line();

            builder.Line("public T Match<T>(");
            builder.Indented(() =>
            {
                foreach (var member in members)
                {
                    var parameterName = CSharpIdentifier.ParameterName(member.Name);
                    var valueType = GetValueType(
                        model,
                        member.Target,
                        nullable: false,
                        currentNamespace: shape.Id.Namespace,
                        baseNamespace: options.BaseNamespace
                    );
                    builder.Line($"Func<{valueType}, T> {parameterName},");
                }

                builder.Line("Func<string, Document, T> unknown)");
            });
            builder.Block(() =>
            {
                foreach (var member in members)
                {
                    var parameterName = CSharpIdentifier.ParameterName(member.Name);
                    builder.Line($"ArgumentNullException.ThrowIfNull({parameterName});");
                }

                builder.Line("ArgumentNullException.ThrowIfNull(unknown);");
                builder.Line();
                builder.Line("return this switch");
                builder.Block(
                    () =>
                    {
                        foreach (var member in members)
                        {
                            var variantName = CSharpIdentifier.TypeName(member.Name);
                            var parameterName = CSharpIdentifier.ParameterName(member.Name);
                            builder.Line($"{variantName} value => {parameterName}(value.Value),");
                        }

                        builder.Line("Unknown value => unknown(value.Tag, value.Value),");
                        builder.Line(
                            "_ => throw new InvalidOperationException(\"Unknown union variant.\"),"
                        );
                    },
                    closingSuffix: ";"
                );
            });
        });
        return builder.ToString();
    }

    private static string GetUnionValueAssignment(
        SmithyModel model,
        ShapeId target,
        string parameterName
    )
    {
        return IsReferenceType(model, target)
            ? $"{parameterName} ?? throw new ArgumentNullException(nameof({parameterName}))"
            : parameterName;
    }

    private static void AppendConstructor(
        CSharpWriter builder,
        SmithyModel model,
        ModelShape shape,
        string typeName,
        CSharpGenerationOptions options,
        string? baseCall
    )
    {
        var members = shape
            .Members.Values.OrderBy(member => member.Name, StringComparer.Ordinal)
            .ToArray();
        builder.Write($"public {typeName}(");
        if (baseCall is not null)
        {
            builder.Write("string? message = null");
            if (members.Length > 0)
            {
                builder.Write(", ");
            }
        }

        builder.Write(
            string.Join(
                ", ",
                members.Select(member =>
                    GetParameter(model, shape, member, shape.Id.Namespace, options)
                )
            )
        );
        builder.Line(")");
        if (baseCall is not null)
        {
            builder.Indented(() =>
            {
                builder.Line($": {baseCall}");
            });
        }

        builder.Block(() =>
        {
            foreach (var member in members)
            {
                var propertyName = CSharpIdentifier.PropertyName(member.Name);
                var parameterName = CSharpIdentifier.ParameterName(member.Name);
                builder.Line(
                    $"{propertyName} = {GetAssignment(model, shape, member, parameterName, shape.Id.Namespace, options)};"
                );
            }
        });

        if (members.Length > 0)
        {
            builder.Line();
        }
    }

    private static void AppendErrorConstructor(
        CSharpWriter builder,
        SmithyModel model,
        ModelShape shape,
        string typeName,
        CSharpGenerationOptions options,
        MemberShape? messageMember
    )
    {
        var members = GetSortedMembers(shape, excludedMember: messageMember).ToArray();
        builder.Write($"public {typeName}(string? message = null");
        if (members.Length > 0)
        {
            builder.Write(", ");
        }

        builder.Write(
            string.Join(
                ", ",
                members.Select(member =>
                    GetParameter(model, shape, member, shape.Id.Namespace, options)
                )
            )
        );
        builder.Line(")");
        builder.Indented(() =>
        {
            builder.Line(": base(message)");
        });

        builder.Block(() =>
        {
            foreach (var member in members)
            {
                var propertyName = CSharpIdentifier.PropertyName(member.Name);
                var parameterName = CSharpIdentifier.ParameterName(member.Name);
                builder.Line(
                    $"{propertyName} = {GetAssignment(model, shape, member, parameterName, shape.Id.Namespace, options)};"
                );
            }
        });

        if (members.Length > 0)
        {
            builder.Line();
        }
    }

    private static void AppendErrorMessageProperty(CSharpWriter builder, MemberShape? messageMember)
    {
        if (messageMember is null)
        {
            return;
        }

        AppendMemberAttributes(builder, messageMember, isSparse: false);
        builder.Line("public override string Message => base.Message;");
        builder.Line();
    }

    private static void AppendProperties(
        CSharpWriter builder,
        SmithyModel model,
        ModelShape shape,
        CSharpGenerationOptions options,
        MemberShape? excludedMember = null
    )
    {
        foreach (var member in GetSortedMembers(shape, excludedMember))
        {
            var propertyName = CSharpIdentifier.PropertyName(member.Name);
            var propertyType = GetMemberType(model, shape, member, shape.Id.Namespace, options);
            AppendMemberAttributes(builder, member, isSparse: IsSparseTarget(model, member.Target));
            builder.Line($"public {propertyType} {propertyName} {{ get; }}");
        }
    }

    private static bool IsSparseTarget(SmithyModel model, ShapeId target)
    {
        return model.Shapes.TryGetValue(target, out var shape)
            && shape.Traits.Has(SmithyPrelude.SparseTrait);
    }

    private static void AppendShapeAttributes(CSharpWriter builder, ModelShape shape)
    {
        builder.Line($"[SmithyShape({FormatString(shape.Id.ToString())}, ShapeKind.{shape.Kind})]");
        AppendTraitAttributes(builder, shape.Traits);
    }

    private static void AppendMemberAttributes(
        CSharpWriter builder,
        MemberShape member,
        bool isSparse
    )
    {
        var arguments = new List<string>
        {
            FormatString(member.Name),
            FormatString(member.Target.ToString()),
        };
        if (member.IsRequired)
        {
            arguments.Add("IsRequired = true");
        }

        if (isSparse)
        {
            arguments.Add("IsSparse = true");
        }

        if (member.Traits.GetValueOrDefault(SmithyPrelude.JsonNameTrait) is { } jsonName)
        {
            arguments.Add($"JsonName = {FormatString(jsonName.AsString())}");
        }

        builder.Line($"[SmithyMember({string.Join(", ", arguments)})]");
        AppendTraitAttributes(builder, member.Traits);
    }

    private static void AppendTraitAttributes(CSharpWriter builder, TraitCollection traits)
    {
        foreach (var trait in traits.OrderBy(trait => trait.Key.ToString(), StringComparer.Ordinal))
        {
            var value = GetTraitAttributeValue(trait.Value);
            var valueInitializer = value is null
                ? string.Empty
                : $", Value = {FormatString(value)}";
            builder.Line($"[SmithyTrait({FormatString(trait.Key.ToString())}{valueInitializer})]");
        }
    }

    private static string? GetTraitAttributeValue(Document value)
    {
        return value.Kind switch
        {
            DocumentKind.Null => null,
            DocumentKind.Boolean => value.AsBoolean() ? "true" : "false",
            DocumentKind.Number => value.AsNumber().ToString(CultureInfo.InvariantCulture),
            DocumentKind.String => value.AsString(),
            _ => null,
        };
    }

    private static IEnumerable<MemberShape> GetSortedMembers(
        ModelShape shape,
        MemberShape? excludedMember = null
    )
    {
        return shape
            .Members.Values.Where(member => !ReferenceEquals(member, excludedMember))
            .OrderBy(member => member.Name, StringComparer.Ordinal);
    }

    private static MemberShape? GetErrorMessageMember(ModelShape shape)
    {
        return
            shape.Members.TryGetValue("message", out var member)
            && member.Target.Namespace == SmithyPrelude.Namespace
            && member.Target.Name == "String"
            ? member
            : null;
    }

    private static string GetParameter(
        SmithyModel model,
        ModelShape container,
        MemberShape member,
        string currentNamespace,
        CSharpGenerationOptions options
    )
    {
        var parameterType = GetMemberParameterType(
            model,
            container,
            member,
            currentNamespace,
            options
        );
        var parameterName = CSharpIdentifier.ParameterName(member.Name);
        var defaultValue =
            IsNullableMember(container, member, options)
            || GetEffectiveDefaultValue(container, member, options) is not null
                ? " = null"
                : string.Empty;
        return $"{parameterType} {parameterName}{defaultValue}";
    }

    private static string GetAssignment(
        SmithyModel model,
        ModelShape container,
        MemberShape member,
        string parameterName,
        string currentNamespace,
        CSharpGenerationOptions options
    )
    {
        var effectiveDefault = GetEffectiveDefaultValue(container, member, options);
        if (effectiveDefault is not null)
        {
            return $"{parameterName} ?? {GetDefaultExpression(model, member.Target, effectiveDefault.Value, currentNamespace, options.BaseNamespace)}";
        }

        if (!IsNullableMember(container, member, options) && IsReferenceType(model, member.Target))
        {
            return $"{parameterName} ?? throw new ArgumentNullException(nameof({parameterName}))";
        }

        return parameterName;
    }

    private static string GetMemberType(
        SmithyModel model,
        ModelShape container,
        MemberShape member,
        string currentNamespace,
        CSharpGenerationOptions options
    )
    {
        return GetValueType(
            model,
            member.Target,
            nullable: IsNullableMember(container, member, options),
            currentNamespace,
            options.BaseNamespace
        );
    }

    private static string GetMemberParameterType(
        SmithyModel model,
        ModelShape container,
        MemberShape member,
        string currentNamespace,
        CSharpGenerationOptions options
    )
    {
        return GetValueType(
            model,
            member.Target,
            nullable: IsNullableMember(container, member, options)
                || GetEffectiveDefaultValue(container, member, options) is not null,
            currentNamespace,
            options.BaseNamespace
        );
    }

    private static bool IsNullableMember(
        ModelShape container,
        MemberShape member,
        CSharpGenerationOptions options
    )
    {
        if (options.NullabilityMode == CSharpNullabilityMode.NonAuthoritative)
        {
            if (container.Traits.Has(SmithyPrelude.InputTrait) || member.IsClientOptional)
            {
                return true;
            }
        }

        return !member.IsRequired && GetEffectiveDefaultValue(container, member, options) is null;
    }

    private static Document? GetEffectiveDefaultValue(
        ModelShape container,
        MemberShape member,
        CSharpGenerationOptions options
    )
    {
        if (member.DefaultValue is not { Kind: not DocumentKind.Null } value)
        {
            return null;
        }

        if (
            options.NullabilityMode == CSharpNullabilityMode.NonAuthoritative
            && (container.Traits.Has(SmithyPrelude.InputTrait) || member.IsClientOptional)
        )
        {
            return null;
        }

        return value;
    }

    private static string GetValueType(
        SmithyModel model,
        ShapeId target,
        bool nullable,
        string currentNamespace,
        string? baseNamespace
    )
    {
        var type = GetNonNullableValueType(model, target, currentNamespace, baseNamespace);
        return nullable ? $"{type}?" : type;
    }

    private static string GetNonNullableValueType(
        SmithyModel model,
        ShapeId target,
        string currentNamespace,
        string? baseNamespace
    )
    {
        if (target.Namespace == SmithyPrelude.Namespace)
        {
            return target.Name switch
            {
                "Blob" => "byte[]",
                "Boolean" => "bool",
                "Byte" => "sbyte",
                "Short" => "short",
                "Integer" => "int",
                "Long" => "long",
                "Float" => "float",
                "Double" => "double",
                "BigInteger" => "System.Numerics.BigInteger",
                "BigDecimal" => "decimal",
                "Timestamp" => "DateTimeOffset",
                "String" => "string",
                "Document" => "Document",
                _ => CSharpIdentifier.TypeName(target.Name),
            };
        }

        return GetTypeReference(target, currentNamespace, baseNamespace);
    }

    private static bool IsReferenceType(SmithyModel model, ShapeId target)
    {
        if (target.Namespace == SmithyPrelude.Namespace)
        {
            return target.Name is "Blob" or "String" or "Document";
        }

        return model.GetShape(target).Kind
            is ShapeKind.Structure
                or ShapeKind.List
                or ShapeKind.Set
                or ShapeKind.Map
                or ShapeKind.Union;
    }

    private static string GetDefaultExpression(
        SmithyModel model,
        ShapeId target,
        Document value,
        string currentNamespace,
        string? baseNamespace
    )
    {
        if (target.Namespace == SmithyPrelude.Namespace)
        {
            return target.Name switch
            {
                "Boolean" => value.AsBoolean() ? "true" : "false",
                "Byte" or "Short" or "Integer" or "Long" => value
                    .AsNumber()
                    .ToString(CultureInfo.InvariantCulture),
                "Float" => string.Create(
                    CultureInfo.InvariantCulture,
                    $"{value.AsNumber().ToString(CultureInfo.InvariantCulture)}f"
                ),
                "Double" => string.Create(
                    CultureInfo.InvariantCulture,
                    $"{value.AsNumber().ToString(CultureInfo.InvariantCulture)}d"
                ),
                "BigDecimal" => string.Create(
                    CultureInfo.InvariantCulture,
                    $"{value.AsNumber().ToString(CultureInfo.InvariantCulture)}m"
                ),
                "String" => FormatString(value.AsString()),
                "Document" => "Document.Null",
                _ => throw new SmithyException(
                    $"Default values for target '{target}' are not supported yet."
                ),
            };
        }

        var targetShape = model.GetShape(target);
        return targetShape.Kind switch
        {
            ShapeKind.Enum =>
                $"new {GetTypeReference(target, currentNamespace, baseNamespace)}({FormatString(value.AsString())})",
            ShapeKind.IntEnum =>
                $"({GetTypeReference(target, currentNamespace, baseNamespace)}){(int)value.AsNumber()}",
            _ => throw new SmithyException(
                $"Default values for target '{target}' are not supported yet."
            ),
        };
    }

    private static CSharpWriter CreateFileBuilder(
        ModelShape shape,
        CSharpGenerationOptions options,
        IReadOnlyList<string>? extraUsings = null
    )
    {
        var builder = new CSharpWriter();
        builder.Line("// <auto-generated />");
        builder.Line("#nullable enable");
        builder.Line();
        builder.Line("using System;");
        builder.Line("using System.Collections.Generic;");
        builder.Line("using System.Linq;");
        builder.Line("using SmithyNet.Core;");
        builder.Line("using SmithyNet.Core.Annotations;");
        foreach (var @namespace in extraUsings ?? [])
        {
            builder.Line($"using {@namespace};");
        }

        builder.Line();
        builder.Line(
            $"namespace {CSharpIdentifier.Namespace(shape.Id.Namespace, options.BaseNamespace)};"
        );
        builder.Line();
        return builder;
    }

    private static string GetPath(ModelShape shape)
    {
        var namespacePath = string.Join(
            '/',
            shape.Id.Namespace.Split('.').Select(CSharpIdentifier.FileSegment)
        );
        return $"{namespacePath}/{GetTypeName(shape.Id)}.g.cs";
    }

    private static string GetClientPath(ModelShape shape)
    {
        var namespacePath = string.Join(
            '/',
            shape.Id.Namespace.Split('.').Select(CSharpIdentifier.FileSegment)
        );
        return $"{namespacePath}/{GetTypeName(shape.Id)}Client.g.cs";
    }

    private static string GetTypeName(ShapeId id)
    {
        return CSharpIdentifier.TypeName(id.Name);
    }

    private static string GetTypeReference(
        ShapeId id,
        string currentNamespace,
        string? baseNamespace
    )
    {
        var typeName = GetTypeName(id);
        return string.Equals(id.Namespace, currentNamespace, StringComparison.Ordinal)
            ? typeName
            : $"global::{CSharpIdentifier.Namespace(id.Namespace, baseNamespace)}.{typeName}";
    }

    private static string FormatString(string value)
    {
        var builder = new StringBuilder(value.Length + 2);
        builder.Append('"');
        foreach (var character in value)
        {
            builder.Append(
                character switch
                {
                    '\\' => "\\\\",
                    '"' => "\\\"",
                    '\0' => "\\0",
                    '\a' => "\\a",
                    '\b' => "\\b",
                    '\f' => "\\f",
                    '\n' => "\\n",
                    '\r' => "\\r",
                    '\t' => "\\t",
                    '\v' => "\\v",
                    _ => character.ToString(),
                }
            );
        }

        builder.Append('"');
        return builder.ToString();
    }

    private sealed record HttpBinding(string Method, string Uri);
}

#pragma warning restore CA1305
