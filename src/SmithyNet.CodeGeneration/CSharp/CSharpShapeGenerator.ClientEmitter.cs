using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using SmithyNet.CodeGeneration.Model;
using SmithyNet.Core;

namespace SmithyNet.CodeGeneration.CSharp;

#pragma warning disable CA1305 // Source text is assembled from Smithy identifiers and explicitly formatted literals.

public sealed partial class CSharpShapeGenerator
{
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
        ModelShape? outputShape = null;
        string? outputType = null;
        if (operation.Output is { } operationOutput)
        {
            outputShape = model.GetShape(operationOutput);
            outputType = GetTypeReference(
                operationOutput,
                service.Id.Namespace,
                options.BaseNamespace
            );
        }

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

            if (outputShape is null || outputType is null)
            {
                builder.Line("return;");
                return;
            }

            builder.Line();
            AppendResponseReturn(
                builder,
                model,
                outputShape,
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
            var headerName = member.Traits[SmithyPrelude.HttpHeaderTrait].AsString();
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
                var queryName = member.Traits[SmithyPrelude.HttpQueryTrait].AsString();
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
            var headerName = member.Traits[SmithyPrelude.HttpHeaderTrait].AsString();
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

    private static bool IsHttpHeaderMember(MemberShape member) =>
        member.Traits.Has(SmithyPrelude.HttpHeaderTrait);

    private static bool IsHttpLabelMember(MemberShape member) =>
        member.Traits.Has(SmithyPrelude.HttpLabelTrait);

    private static bool IsHttpPayloadMember(MemberShape member) =>
        member.Traits.Has(SmithyPrelude.HttpPayloadTrait);

    private static bool IsHttpQueryMember(MemberShape member) =>
        member.Traits.Has(SmithyPrelude.HttpQueryTrait);

    private static bool IsHttpResponseCodeMember(MemberShape member) =>
        member.Traits.Has(SmithyPrelude.HttpResponseCodeTrait);

    private static bool HasResponseBindings(ModelShape output)
    {
        return output.Members.Values.Any(member =>
            IsHttpHeaderMember(member)
            || IsHttpPayloadMember(member)
            || IsHttpResponseCodeMember(member)
        );
    }

    private static bool TryGetPayloadMember(
        ModelShape input,
        [NotNullWhen(true)] out MemberShape? payloadMember
    )
    {
        var payloadMembers = input.Members.Values.Where(IsHttpPayloadMember).ToArray();
        if (payloadMembers.Length > 1)
        {
            throw new SmithyException(
                $"Input shape '{input.Id}' has multiple @httpPayload members."
            );
        }

        payloadMember = payloadMembers.FirstOrDefault();
        return payloadMember is not null;
    }

    private static bool HasHttpBody(ModelShape input) => input.Members.Values.Any(IsHttpBodyMember);

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
                builder.Line(
                    "if (!Enum.TryParse(targetType, value, ignoreCase: false, out var enumResult))"
                );
                builder.Block(() =>
                {
                    builder.Line(
                        "throw new InvalidOperationException($\"Unknown enum value '{value}' for type '{targetType.Name}'.\");"
                    );
                });
                builder.Line("return (T)enumResult!;");
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
}

#pragma warning restore CA1305
