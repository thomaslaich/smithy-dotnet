using System.Globalization;
using SmithyNet.CodeGeneration.Model;
using SmithyNet.Core;

namespace SmithyNet.CodeGeneration.CSharp;

#pragma warning disable CA1305 // Source text is assembled from Smithy identifiers and explicitly formatted literals.

public sealed partial class CSharpShapeGenerator
{
    private static GeneratedCSharpFile GenerateServer(
        SmithyModel model,
        ModelShape service,
        CSharpGenerationOptions options
    )
    {
        var builder = CreateFileBuilder(
            service,
            options,
            [
                "System.Globalization",
                "System.IO",
                "Microsoft.AspNetCore.Builder",
                "Microsoft.AspNetCore.Http",
                "Microsoft.AspNetCore.Routing",
                "SmithyNet.Json",
                "SmithyNet.Server",
                "System.Threading",
                "System.Threading.Tasks",
            ]
        );
        var serviceTypeName = GetTypeName(service.Id);
        var interfaceName = $"I{serviceTypeName}ServiceHandler";
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
                builder.Line($"{GetServerOperationSignature(service, operation, options)};");
            }
        });
        builder.Line();
        AppendServerRegistrationExtensions(
            builder,
            model,
            service,
            serviceTypeName,
            interfaceName,
            options
        );
        builder.Line();
        AppendAspNetCoreEndpointExtensions(
            builder,
            model,
            service,
            serviceTypeName,
            interfaceName,
            options
        );

        return new GeneratedCSharpFile(GetServerPath(service), builder.ToString());
    }

    private static void AppendServerRegistrationExtensions(
        CSharpWriter builder,
        SmithyModel model,
        ModelShape service,
        string serviceTypeName,
        string interfaceName,
        CSharpGenerationOptions options
    )
    {
        builder.Line($"public static class {serviceTypeName}ServerExtensions");
        builder.Block(() =>
        {
            builder.Line(
                $"public static SmithyServerDispatcher Register{serviceTypeName}Service(this SmithyServerDispatcher dispatcher, {interfaceName} handler)"
            );
            builder.Block(() =>
            {
                builder.Line("ArgumentNullException.ThrowIfNull(dispatcher);");
                builder.Line("ArgumentNullException.ThrowIfNull(handler);");
                builder.Line();
                foreach (
                    var operationId in service.Operations.OrderBy(
                        id => id.ToString(),
                        StringComparer.Ordinal
                    )
                )
                {
                    var operation = model.GetShape(operationId);
                    AppendServerOperationRegistration(builder, service, operation, options);
                    builder.Line();
                }

                builder.Line("return dispatcher;");
            });
        });
    }

    private static void AppendServerOperationRegistration(
        CSharpWriter builder,
        ModelShape service,
        ModelShape operation,
        CSharpGenerationOptions options
    )
    {
        var methodName = $"{CSharpIdentifier.PropertyName(operation.Id.Name)}Async";
        builder.Line(
            $"dispatcher.Register({FormatString(service.Id.Name)}, {FormatString(operation.Id.Name)}, async (request, cancellationToken) =>"
        );
        builder.Block(
            () =>
            {
                var hasOutput = operation.Output is not null;
                if (operation.Input is { } input)
                {
                    var inputType = GetTypeReference(
                        input,
                        service.Id.Namespace,
                        options.BaseNamespace
                    );
                    builder.Line(
                        $"var input = request.Input as {inputType} ?? throw new ArgumentException({FormatString($"Expected input type '{inputType}' for operation '{operation.Id.Name}'.")}, nameof(request));"
                    );
                    builder.Line(
                        hasOutput
                            ? $"var output = await handler.{methodName}(input, cancellationToken).ConfigureAwait(false);"
                            : $"await handler.{methodName}(input, cancellationToken).ConfigureAwait(false);"
                    );
                }
                else
                {
                    builder.Line(
                        hasOutput
                            ? $"var output = await handler.{methodName}(cancellationToken).ConfigureAwait(false);"
                            : $"await handler.{methodName}(cancellationToken).ConfigureAwait(false);"
                    );
                }

                var outputExpression = hasOutput ? "output" : "null";
                builder.Line(
                    $"return new SmithyServerResponse(request.ServiceName, request.OperationName, {outputExpression});"
                );
            },
            closingSuffix: ");"
        );
    }

    private static void AppendAspNetCoreEndpointExtensions(
        CSharpWriter builder,
        SmithyModel model,
        ModelShape service,
        string serviceTypeName,
        string interfaceName,
        CSharpGenerationOptions options
    )
    {
        builder.Line($"public static class {serviceTypeName}AspNetCoreExtensions");
        builder.Block(() =>
        {
            builder.Line(
                $"public static IEndpointRouteBuilder Map{serviceTypeName}Service(this IEndpointRouteBuilder endpoints)"
            );
            builder.Block(() =>
            {
                builder.Line("ArgumentNullException.ThrowIfNull(endpoints);");
                builder.Line();
                foreach (
                    var operationId in service.Operations.OrderBy(
                        id => id.ToString(),
                        StringComparer.Ordinal
                    )
                )
                {
                    var operation = model.GetShape(operationId);
                    AppendAspNetCoreOperationMap(
                        builder,
                        model,
                        service,
                        operation,
                        interfaceName,
                        options
                    );
                    builder.Line();
                }

                builder.Line("return endpoints;");
            });
            builder.Line();
            AppendAspNetCoreBoundResponseWriters(builder, model, service, options);
            builder.Line();
            AppendAspNetCoreHelpers(builder);
        });
    }

    private static void AppendAspNetCoreOperationMap(
        CSharpWriter builder,
        SmithyModel model,
        ModelShape service,
        ModelShape operation,
        string interfaceName,
        CSharpGenerationOptions options
    )
    {
        var httpBinding = ReadHttpBinding(operation);
        var methodName = $"{CSharpIdentifier.PropertyName(operation.Id.Name)}Async";
        builder.Line(
            $"endpoints.MapMethods({FormatString(httpBinding.Uri)}, [{FormatString(httpBinding.Method)}], async (HttpContext httpContext, {interfaceName} handler, CancellationToken cancellationToken) =>"
        );
        builder.Block(
            () =>
            {
                builder.Line("ArgumentNullException.ThrowIfNull(httpContext);");
                builder.Line("ArgumentNullException.ThrowIfNull(handler);");
                builder.Line();
                if (HasHttpErrorHandlers(model, operation))
                {
                    builder.Line("try");
                    builder.Block(() =>
                    {
                        AppendAspNetCoreOperationBody(
                            builder,
                            model,
                            service,
                            operation,
                            methodName,
                            options
                        );
                    });
                    AppendAspNetCoreErrorHandlers(
                        builder,
                        model,
                        operation,
                        service.Id.Namespace,
                        options
                    );
                }
                else
                {
                    AppendAspNetCoreOperationBody(
                        builder,
                        model,
                        service,
                        operation,
                        methodName,
                        options
                    );
                }
            },
            closingSuffix: ");"
        );
    }

    private static void AppendAspNetCoreOperationBody(
        CSharpWriter builder,
        SmithyModel model,
        ModelShape service,
        ModelShape operation,
        string methodName,
        CSharpGenerationOptions options
    )
    {
        if (operation.Input is { } inputId)
        {
            var inputShape = model.GetShape(inputId);
            var inputType = GetTypeReference(inputId, service.Id.Namespace, options.BaseNamespace);
            AppendAspNetCoreInputBinding(builder, model, service, inputShape, inputType, options);
            builder.Line(
                operation.Output is null
                    ? $"await handler.{methodName}(input, cancellationToken).ConfigureAwait(false);"
                    : $"var output = await handler.{methodName}(input, cancellationToken).ConfigureAwait(false);"
            );
        }
        else
        {
            builder.Line(
                operation.Output is null
                    ? $"await handler.{methodName}(cancellationToken).ConfigureAwait(false);"
                    : $"var output = await handler.{methodName}(cancellationToken).ConfigureAwait(false);"
            );
        }

        builder.Line(
            operation.Output is null
                ? "httpContext.Response.StatusCode = StatusCodes.Status204NoContent;"
                : GetAspNetCoreOutputResponseStatement(model, operation)
        );
    }

    private static string GetAspNetCoreOutputResponseStatement(
        SmithyModel model,
        ModelShape operation
    )
    {
        if (operation.Output is not { } outputId)
        {
            return "httpContext.Response.StatusCode = StatusCodes.Status204NoContent;";
        }

        var output = model.GetShape(outputId);
        return HasResponseBindings(output)
            ? "await WriteBoundResponseAsync(httpContext, output, cancellationToken).ConfigureAwait(false);"
            : "await WriteJsonResponseAsync(httpContext, output, cancellationToken).ConfigureAwait(false);";
    }

    private static void AppendAspNetCoreBoundResponseWriters(
        CSharpWriter builder,
        SmithyModel model,
        ModelShape service,
        CSharpGenerationOptions options
    )
    {
        var outputShapes = service
            .Operations.Select(operationId => model.GetShape(operationId).Output)
            .OfType<ShapeId>()
            .Distinct()
            .Select(outputId => model.GetShape(outputId))
            .Where(HasResponseBindings)
            .OrderBy(shape => shape.Id.ToString(), StringComparer.Ordinal)
            .ToArray();
        foreach (var output in outputShapes)
        {
            AppendAspNetCoreBoundResponseWriter(builder, output, service.Id.Namespace, options);
            builder.Line();
        }
    }

    private static void AppendAspNetCoreBoundResponseWriter(
        CSharpWriter builder,
        ModelShape output,
        string currentNamespace,
        CSharpGenerationOptions options
    )
    {
        var outputType = GetTypeReference(output.Id, currentNamespace, options.BaseNamespace);
        builder.Line(
            $"private static async Task WriteBoundResponseAsync(HttpContext httpContext, {outputType} output, CancellationToken cancellationToken)"
        );
        builder.Block(() =>
        {
            builder.Line("ArgumentNullException.ThrowIfNull(output);");
            foreach (var member in GetSortedMembers(output).Where(IsHttpResponseCodeMember))
            {
                var propertyName = CSharpIdentifier.PropertyName(member.Name);
                builder.Line(
                    $"httpContext.Response.StatusCode = Convert.ToInt32(output.{propertyName}, CultureInfo.InvariantCulture);"
                );
            }

            foreach (var member in GetSortedMembers(output).Where(IsHttpHeaderMember))
            {
                var propertyName = CSharpIdentifier.PropertyName(member.Name);
                var headerName = member.Traits[SmithyPrelude.HttpHeaderTrait].AsString();
                builder.Line(
                    $"AddResponseHeader(httpContext, {FormatString(headerName)}, output.{propertyName});"
                );
            }

            if (GetSortedMembers(output).FirstOrDefault(IsHttpPayloadMember) is { } payloadMember)
            {
                var propertyName = CSharpIdentifier.PropertyName(payloadMember.Name);
                builder.Line(
                    $"await WriteJsonResponseAsync(httpContext, output.{propertyName}, cancellationToken).ConfigureAwait(false);"
                );
                return;
            }

            var bodyMembers = GetSortedMembers(output)
                .Where(member => IsHttpBodyMember(member) && !IsHttpResponseCodeMember(member))
                .ToArray();
            if (bodyMembers.Length == 0)
            {
                return;
            }

            builder.Line("var responseBody = new Dictionary<string, object?>");
            builder.Block(
                () =>
                {
                    foreach (var member in bodyMembers)
                    {
                        var propertyName = CSharpIdentifier.PropertyName(member.Name);
                        var jsonName =
                            member.Traits.GetValueOrDefault(SmithyPrelude.JsonNameTrait)?.AsString()
                            ?? member.Name;
                        builder.Line($"[{FormatString(jsonName)}] = output.{propertyName},");
                    }
                },
                closingSuffix: ";"
            );
            builder.Line(
                "await WriteJsonResponseAsync(httpContext, responseBody, cancellationToken).ConfigureAwait(false);"
            );
        });
    }

    private static void AppendAspNetCoreInputBinding(
        CSharpWriter builder,
        SmithyModel model,
        ModelShape service,
        ModelShape input,
        string inputType,
        CSharpGenerationOptions options
    )
    {
        var members = GetSortedMembers(input).ToArray();
        if (members.Length == 0)
        {
            builder.Line($"var input = new {inputType}();");
            return;
        }

        builder.Line($"var input = new {inputType}(");
        builder.Indented(() =>
        {
            for (var i = 0; i < members.Length; i++)
            {
                var suffix = i == members.Length - 1 ? string.Empty : ",";
                builder.Line(
                    $"{GetAspNetCoreInputMemberExpression(model, input, members[i], service.Id.Namespace, options)}{suffix}"
                );
            }
        });
        builder.Line(");");
    }

    private static string GetAspNetCoreInputMemberExpression(
        SmithyModel model,
        ModelShape input,
        MemberShape member,
        string currentNamespace,
        CSharpGenerationOptions options
    )
    {
        var memberType = GetMemberParameterType(model, input, member, currentNamespace, options);
        if (IsHttpLabelMember(member))
        {
            return $"GetRouteValue<{memberType}>(httpContext, {FormatString(member.Name)})";
        }

        if (IsHttpQueryMember(member))
        {
            var queryName = member.Traits[SmithyPrelude.HttpQueryTrait].AsString();
            return $"GetQueryValue<{memberType}>(httpContext, {FormatString(queryName)})";
        }

        if (IsHttpHeaderMember(member))
        {
            var headerName = member.Traits[SmithyPrelude.HttpHeaderTrait].AsString();
            return $"GetHeaderValue<{memberType}>(httpContext, {FormatString(headerName)})";
        }

        if (IsHttpPayloadMember(member))
        {
            return $"await ReadJsonRequestBodyAsync<{memberType}>(httpContext, cancellationToken).ConfigureAwait(false)";
        }

        var jsonName =
            member.Traits.GetValueOrDefault(SmithyPrelude.JsonNameTrait)?.AsString() ?? member.Name;
        return $"await ReadJsonRequestBodyMemberAsync<{memberType}>(httpContext, {FormatString(jsonName)}, cancellationToken).ConfigureAwait(false)";
    }

    private static void AppendAspNetCoreErrorHandlers(
        CSharpWriter builder,
        SmithyModel model,
        ModelShape operation,
        string currentNamespace,
        CSharpGenerationOptions options
    )
    {
        foreach (
            var errorId in operation.Errors.OrderBy(id => id.ToString(), StringComparer.Ordinal)
        )
        {
            var error = model.GetShape(errorId);
            if (GetHttpErrorCode(error) is not { } statusCode)
            {
                continue;
            }

            var errorType = GetTypeReference(errorId, currentNamespace, options.BaseNamespace);
            builder.Line($"catch ({errorType} error)");
            builder.Block(() =>
            {
                builder.Line(
                    $"httpContext.Response.StatusCode = {statusCode.ToString(CultureInfo.InvariantCulture)};"
                );
                builder.Line(
                    "await WriteJsonResponseAsync(httpContext, error, cancellationToken).ConfigureAwait(false);"
                );
            });
        }
    }

    private static bool HasHttpErrorHandlers(SmithyModel model, ModelShape operation)
    {
        return operation.Errors.Any(errorId =>
            GetHttpErrorCode(model.GetShape(errorId)) is not null
        );
    }

    private static void AppendAspNetCoreHelpers(CSharpWriter builder)
    {
        builder.Line(
            "private static async Task WriteBoundResponseAsync<T>(HttpContext httpContext, T output, CancellationToken cancellationToken)"
        );
        builder.Block(() =>
        {
            builder.Line("ArgumentNullException.ThrowIfNull(output);");
            builder.Line(
                "await WriteJsonResponseAsync(httpContext, output, cancellationToken).ConfigureAwait(false);"
            );
        });
        builder.Line();
        builder.Line("private static T GetRouteValue<T>(HttpContext httpContext, string name)");
        builder.Block(() =>
        {
            builder.Line(
                "return httpContext.Request.RouteValues.TryGetValue(name, out var value) && value is not null"
            );
            builder.Indented(() =>
            {
                builder.Line("? ConvertHttpValue<T>(value.ToString())");
                builder.Line(
                    ": throw new InvalidOperationException($\"Missing route value '{name}'.\");"
                );
            });
        });
        builder.Line();
        builder.Line("private static T GetQueryValue<T>(HttpContext httpContext, string name)");
        builder.Block(() =>
        {
            builder.Line("return httpContext.Request.Query.TryGetValue(name, out var values)");
            builder.Indented(() =>
            {
                builder.Line("? ConvertHttpValue<T>(values.FirstOrDefault())");
                builder.Line(": default!;");
            });
        });
        builder.Line();
        builder.Line("private static T GetHeaderValue<T>(HttpContext httpContext, string name)");
        builder.Block(() =>
        {
            builder.Line("return httpContext.Request.Headers.TryGetValue(name, out var values)");
            builder.Indented(() =>
            {
                builder.Line("? ConvertHttpValue<T>(values.FirstOrDefault())");
                builder.Line(": default!;");
            });
        });
        builder.Line();
        builder.Line(
            "private static async Task<T> ReadJsonRequestBodyAsync<T>(HttpContext httpContext, CancellationToken cancellationToken)"
        );
        builder.Block(() =>
        {
            builder.Line("using var reader = new StreamReader(httpContext.Request.Body);");
            builder.Line(
                "var content = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);"
            );
            builder.Line("return SmithyJsonSerializer.Deserialize<T>(content);");
        });
        builder.Line();
        builder.Line(
            "private static async Task<T> ReadJsonRequestBodyMemberAsync<T>(HttpContext httpContext, string name, CancellationToken cancellationToken)"
        );
        builder.Block(() =>
        {
            builder.Line("using var reader = new StreamReader(httpContext.Request.Body);");
            builder.Line(
                "var content = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);"
            );
            builder.Line("if (string.IsNullOrWhiteSpace(content))");
            builder.Block(() =>
            {
                builder.Line("return default!;");
            });
            builder.Line();
            builder.Line("using var document = System.Text.Json.JsonDocument.Parse(content);");
            builder.Line("return document.RootElement.TryGetProperty(name, out var value)");
            builder.Indented(() =>
            {
                builder.Line("? SmithyJsonSerializer.Deserialize<T>(value.GetRawText())");
                builder.Line(": default!;");
            });
        });
        builder.Line();
        builder.Line(
            "private static async Task WriteJsonResponseAsync<T>(HttpContext httpContext, T value, CancellationToken cancellationToken)"
        );
        builder.Block(() =>
        {
            builder.Line("httpContext.Response.ContentType = \"application/json\";");
            builder.Line(
                "await httpContext.Response.WriteAsync(SmithyJsonSerializer.Serialize(value), cancellationToken).ConfigureAwait(false);"
            );
        });
        builder.Line();
        builder.Line(
            "private static void AddResponseHeader(HttpContext httpContext, string name, object? value)"
        );
        builder.Block(() =>
        {
            builder.Line("if (value is null)");
            builder.Block(() =>
            {
                builder.Line("return;");
            });
            builder.Line();
            builder.Line("httpContext.Response.Headers[name] = FormatHttpValue(value);");
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
        builder.Line("private static T ConvertHttpValue<T>(string? value)");
        builder.Block(() =>
        {
            builder.Line("if (value is null)");
            builder.Block(() =>
            {
                builder.Line("return default!;");
            });
            builder.Line();
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
    }

    private static string GetServerOperationSignature(
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
}

#pragma warning restore CA1305
