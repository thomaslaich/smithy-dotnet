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
                "Microsoft.AspNetCore.Builder",
                "Microsoft.AspNetCore.Http",
                "Microsoft.AspNetCore.Routing",
                "SmithyNet.Server.AspNetCore",
                "SmithyNet.Server",
                "System.Threading",
                "System.Threading.Tasks",
            ]
        );
        var serviceTypeName = GetTypeName(service.Id);
        var serviceContractName = GetServiceContractName(serviceTypeName);
        var interfaceName = $"I{serviceContractName}Handler";
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
            serviceContractName,
            interfaceName,
            options
        );
        builder.Line();
        AppendAspNetCoreEndpointExtensions(
            builder,
            model,
            service,
            serviceContractName,
            interfaceName,
            options
        );

        return new GeneratedCSharpFile(GetServerPath(service), builder.ToString());
    }

    private static string GetServiceContractName(string serviceTypeName)
    {
        return serviceTypeName.EndsWith("Service", StringComparison.Ordinal)
            ? serviceTypeName
            : $"{serviceTypeName}Service";
    }

    private static void AppendServerRegistrationExtensions(
        CSharpWriter builder,
        SmithyModel model,
        ModelShape service,
        string serviceContractName,
        string interfaceName,
        CSharpGenerationOptions options
    )
    {
        builder.Line($"public static class {serviceContractName}ServerExtensions");
        builder.Block(() =>
        {
            builder.Line(
                $"public static SmithyServerDispatcher Register{serviceContractName}(this SmithyServerDispatcher dispatcher, {interfaceName} handler)"
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
        string serviceContractName,
        string interfaceName,
        CSharpGenerationOptions options
    )
    {
        builder.Line($"public static class {serviceContractName}AspNetCoreExtensions");
        builder.Block(() =>
        {
            builder.Line(
                $"public static IEndpointRouteBuilder Map{serviceContractName}(this IEndpointRouteBuilder endpoints)"
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
            AppendAspNetCoreBoundResponseWriters(builder, model, service, options);
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
            : "await SmithyAspNetCoreProtocol.WriteJsonResponseAsync(httpContext, output, cancellationToken).ConfigureAwait(false);";
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
                    $"SmithyAspNetCoreProtocol.SetStatusCode(httpContext, output.{propertyName});"
                );
            }

            foreach (var member in GetSortedMembers(output).Where(IsHttpHeaderMember))
            {
                var propertyName = CSharpIdentifier.PropertyName(member.Name);
                var headerName = member.Traits[SmithyPrelude.HttpHeaderTrait].AsString();
                builder.Line(
                    $"SmithyAspNetCoreProtocol.AddResponseHeader(httpContext, {FormatString(headerName)}, output.{propertyName});"
                );
            }

            if (GetSortedMembers(output).FirstOrDefault(IsHttpPayloadMember) is { } payloadMember)
            {
                var propertyName = CSharpIdentifier.PropertyName(payloadMember.Name);
                builder.Line(
                    $"await SmithyAspNetCoreProtocol.WriteJsonResponseAsync(httpContext, output.{propertyName}, cancellationToken).ConfigureAwait(false);"
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
                "await SmithyAspNetCoreProtocol.WriteJsonResponseAsync(httpContext, responseBody, cancellationToken).ConfigureAwait(false);"
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
            return $"SmithyAspNetCoreProtocol.GetRouteValue<{memberType}>(httpContext, {FormatString(member.Name)})";
        }

        if (IsHttpQueryMember(member))
        {
            var queryName = member.Traits[SmithyPrelude.HttpQueryTrait].AsString();
            return $"SmithyAspNetCoreProtocol.GetQueryValue<{memberType}>(httpContext, {FormatString(queryName)})";
        }

        if (IsHttpHeaderMember(member))
        {
            var headerName = member.Traits[SmithyPrelude.HttpHeaderTrait].AsString();
            return $"SmithyAspNetCoreProtocol.GetHeaderValue<{memberType}>(httpContext, {FormatString(headerName)})";
        }

        if (IsHttpPayloadMember(member))
        {
            return $"await SmithyAspNetCoreProtocol.ReadJsonRequestBodyAsync<{memberType}>(httpContext, cancellationToken).ConfigureAwait(false)";
        }

        var jsonName =
            member.Traits.GetValueOrDefault(SmithyPrelude.JsonNameTrait)?.AsString() ?? member.Name;
        return $"await SmithyAspNetCoreProtocol.ReadJsonRequestBodyMemberAsync<{memberType}>(httpContext, {FormatString(jsonName)}, cancellationToken).ConfigureAwait(false)";
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
                    "await SmithyAspNetCoreProtocol.WriteJsonResponseAsync(httpContext, error, cancellationToken).ConfigureAwait(false);"
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
