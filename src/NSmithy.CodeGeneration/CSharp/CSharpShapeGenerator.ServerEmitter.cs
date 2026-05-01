using System.Globalization;
using Nest.Text;
using NSmithy.CodeGeneration.Model;
using NSmithy.Core;
using NSmithy.Core.Traits;

namespace NSmithy.CodeGeneration.CSharp;

#pragma warning disable CA1305 // Source text is assembled from Smithy identifiers and explicitly formatted literals.

public sealed partial class CSharpShapeGenerator
{
    private static GeneratedCSharpFile GenerateServer(SmithyModel model, ModelShape service, CSharpGenerationOptions options)
    {
        var emitsAspNetCore = service.Traits.Has(SmithyPrelude.SimpleRestJsonTrait);
        var emitsGrpc = service.Traits.Has(SmithyPrelude.GrpcTrait);
        var extraUsings = new List<string>
        {
            "Microsoft.Extensions.DependencyInjection",
            "NSmithy.Server",
            "System.Threading",
            "System.Threading.Tasks",
        };
        if (emitsAspNetCore)
        {
            extraUsings.Add("Microsoft.AspNetCore.Builder");
            extraUsings.Add("Microsoft.AspNetCore.Http");
            extraUsings.Add("Microsoft.AspNetCore.Routing");
            extraUsings.Add("NSmithy.Server.AspNetCore");
        }

        if (emitsGrpc)
        {
            extraUsings.Add("Microsoft.AspNetCore.Builder");
            extraUsings.Add("Microsoft.AspNetCore.Routing");
            extraUsings.Add("Google.Protobuf.WellKnownTypes");
            extraUsings.Add("Grpc.Core");
        }

        var _ = CreateTextFileBuilder(service, options, extraUsings.Distinct(StringComparer.Ordinal).ToArray());
        var serviceTypeName = GetTypeName(service.Id);
        var serviceContractName = GetServiceContractName(serviceTypeName);
        var interfaceName = $"I{serviceContractName}Handler";
        var operationIds = service.Operations.OrderBy(id => id.ToString(), StringComparer.Ordinal).ToArray();
        foreach (var operationId in operationIds)
        {
            var operation = model.GetShape(operationId);
            AddServerOperationInterface(_, service, operation, options);
            _.L();
        }

        var operationInterfaceNames = operationIds
            .Select(operationId => GetOperationHandlerInterfaceName(model.GetShape(operationId)))
            .ToArray();
        var inheritedInterfaces = operationInterfaceNames.Length == 0 ? string.Empty : $" : {string.Join(", ", operationInterfaceNames)}";
        _.L($"public interface {interfaceName}{inheritedInterfaces} {{ }}");
        _.L();
        AddServerDescriptors(_, model, service, serviceContractName, interfaceName, options);
        _.L();
        AddServerExtensions(_, model, service, serviceContractName, interfaceName);
        if (emitsAspNetCore)
        {
            _.L();
            AddAspNetCoreEndpointExtensions(_, model, service, serviceContractName, options);
        }
        if (emitsGrpc)
        {
            _.L();
            AddGrpcEndpointExtensions(_, serviceContractName);
            _.L();
            AddGrpcAdapter(_, model, service, serviceTypeName, serviceContractName, interfaceName, options);
        }

        return new GeneratedCSharpFile(GetServerPath(service), FormatGeneratedText(_));
    }

    private static void AddServerOperationInterface(
        ITextBuilder _,
        ModelShape service,
        ModelShape operation,
        CSharpGenerationOptions options
    )
    {
        _.L($"public interface {GetOperationHandlerInterfaceName(operation)}")
            .B(_ =>
            {
                _.L($"{GetServerOperationSignature(service, operation, options)};");
            });
    }

    private static string GetServiceContractName(string serviceTypeName)
    {
        return serviceTypeName.EndsWith("Service", StringComparison.Ordinal) ? serviceTypeName : $"{serviceTypeName}Service";
    }

    private static string GetOperationHandlerInterfaceName(ModelShape operation)
    {
        return $"I{CSharpIdentifier.TypeName(operation.Id.Name)}Handler";
    }

    private static void AddServerExtensions(
        ITextBuilder _,
        SmithyModel model,
        ModelShape service,
        string serviceContractName,
        string interfaceName
    )
    {
        _.L($"public static class {serviceContractName}ServerExtensions")
            .B(_ =>
            {
                AddServiceCollectionRegistration(_, model, service, serviceContractName, interfaceName);
            });
    }

    private static void AddServerDescriptors(
        ITextBuilder _,
        SmithyModel model,
        ModelShape service,
        string serviceContractName,
        string interfaceName,
        CSharpGenerationOptions options
    )
    {
        _.L($"public static class {serviceContractName}Descriptor")
            .B(_ =>
            {
                foreach (var operationId in service.Operations.OrderBy(id => id.ToString(), StringComparer.Ordinal))
                {
                    AddServerOperationDescriptor(_, service, model.GetShape(operationId), options);
                    _.L();
                }

                _.L($"public static SmithyServiceDescriptor<{interfaceName}> Service {{ get; }} = new(")
                    .B(
                        _ =>
                        {
                            _.L($"{FormatString(service.Id.ToString())},");
                            _.L($"{FormatString(service.Id.Name)},");
                            _.L($"{FormatTraitDescriptors(service.Traits)},");
                            _.L("[")
                                .B(
                                    _ =>
                                    {
                                        foreach (var operationId in service.Operations.OrderBy(id => id.ToString(), StringComparer.Ordinal))
                                        {
                                            _.L($"{CSharpIdentifier.TypeName(operationId.Name)},");
                                        }
                                    },
                                    ConfigureTextBlock(BlockStyle.IndentOnly)
                                );
                            _.L("]");
                        },
                        ConfigureTextBlock(BlockStyle.IndentOnly)
                    );
                _.L(");");
            });
    }

    private static void AddServerOperationDescriptor(
        ITextBuilder _,
        ModelShape service,
        ModelShape operation,
        CSharpGenerationOptions options
    )
    {
        var descriptorName = CSharpIdentifier.TypeName(operation.Id.Name);
        var methodName = $"{CSharpIdentifier.PropertyName(operation.Id.Name)}Async";
        var operationInterfaceName = GetOperationHandlerInterfaceName(operation);
        var inputType = operation.Input is { } input ? GetTypeReference(input, service.Id.Namespace, options.BaseNamespace) : "SmithyUnit";
        var outputType = operation.Output is { } output
            ? GetTypeReference(output, service.Id.Namespace, options.BaseNamespace)
            : "SmithyUnit";

        _.L(
                $"public static SmithyOperationDescriptor<{operationInterfaceName}, {inputType}, {outputType}> {descriptorName} {{ get; }} = new("
            )
            .B(
                _ =>
                {
                    _.L($"{FormatString(operation.Id.ToString())},");
                    _.L($"{FormatString(operation.Id.Name)},");
                    _.L($"{FormatTraitDescriptors(operation.Traits)},");
                    if (operation.Input is not null && operation.Output is not null)
                    {
                        _.L($"static (handler, input, cancellationToken) => handler.{methodName}(input, cancellationToken));");
                    }
                    else if (operation.Input is not null)
                    {
                        _.L("static async (handler, input, cancellationToken) =>")
                            .B(
                                _ =>
                                {
                                    _.L($"await handler.{methodName}(input, cancellationToken).ConfigureAwait(false);");
                                    _.L("return SmithyUnit.Value;");
                                },
                                ConfigureTextBlock(BlockStyle.CurlyBraces)
                            );
                        _.L(");");
                    }
                    else if (operation.Output is not null)
                    {
                        _.L($"static (handler, _, cancellationToken) => handler.{methodName}(cancellationToken));");
                    }
                    else
                    {
                        _.L("static async (handler, _, cancellationToken) =>")
                            .B(
                                _ =>
                                {
                                    _.L($"await handler.{methodName}(cancellationToken).ConfigureAwait(false);");
                                    _.L("return SmithyUnit.Value;");
                                },
                                ConfigureTextBlock(BlockStyle.CurlyBraces)
                            );
                        _.L(");");
                    }
                },
                ConfigureTextBlock(BlockStyle.IndentOnly)
            );
    }

    private static string FormatTraitDescriptors(TraitCollection traits)
    {
        if (traits.Count == 0)
        {
            return "[]";
        }

        return "["
            + string.Join(
                ", ",
                traits
                    .OrderBy(trait => trait.Key.ToString(), StringComparer.Ordinal)
                    .Select(trait =>
                    {
                        var value = GetTraitAttributeValue(trait.Value);
                        return value is null
                            ? $"new SmithyTraitDescriptor({FormatString(trait.Key.ToString())})"
                            : $"new SmithyTraitDescriptor({FormatString(trait.Key.ToString())}, {FormatString(value)})";
                    })
            )
            + "]";
    }

    private static void AddServiceCollectionRegistration(
        ITextBuilder _,
        SmithyModel model,
        ModelShape service,
        string serviceContractName,
        string interfaceName
    )
    {
        _.L($"public static IServiceCollection Add{serviceContractName}Handler<THandler>(this IServiceCollection services)");
        _.L($"    where THandler : class, {interfaceName}")
            .B(_ =>
            {
                _.L("ArgumentNullException.ThrowIfNull(services);");
                _.L();
                _.L("services.AddSingleton<THandler>();");
                _.L($"services.AddSingleton<{interfaceName}>(serviceProvider => serviceProvider.GetRequiredService<THandler>());");
                foreach (var operationId in service.Operations.OrderBy(id => id.ToString(), StringComparer.Ordinal))
                {
                    var operation = model.GetShape(operationId);
                    var operationInterfaceName = GetOperationHandlerInterfaceName(operation);
                    _.L(
                        $"services.AddSingleton<{operationInterfaceName}>(serviceProvider => serviceProvider.GetRequiredService<THandler>());"
                    );
                }
                _.L("return services;");
            });
    }

    private static void AddAspNetCoreEndpointExtensions(
        ITextBuilder _,
        SmithyModel model,
        ModelShape service,
        string serviceContractName,
        CSharpGenerationOptions options
    )
    {
        _.L($"public static class {serviceContractName}AspNetCoreExtensions")
            .B(_ =>
            {
                _.L($"public static IEndpointRouteBuilder Map{serviceContractName}Http(this IEndpointRouteBuilder endpoints)")
                    .B(_ =>
                    {
                        _.L("ArgumentNullException.ThrowIfNull(endpoints);");
                        _.L();
                        foreach (var operationId in service.Operations.OrderBy(id => id.ToString(), StringComparer.Ordinal))
                        {
                            var operation = model.GetShape(operationId);
                            AddAspNetCoreOperationMap(_, model, service, operation, options);
                            _.L();
                        }

                        _.L("return endpoints;");
                    });
                AddAspNetCoreBoundResponseWriters(_, model, service, options);
                AddAspNetCoreBodyProjectionTypes(_, model, service, options);
            });
    }

    private static void AddAspNetCoreOperationMap(
        ITextBuilder _,
        SmithyModel model,
        ModelShape service,
        ModelShape operation,
        CSharpGenerationOptions options
    )
    {
        var httpBinding = ReadHttpBinding(operation);
        var operationInterfaceName = GetOperationHandlerInterfaceName(operation);
        _.L(
                $"endpoints.MapMethods({FormatString(httpBinding.Uri)}, [{FormatString(httpBinding.Method)}], async (HttpContext httpContext, {operationInterfaceName} handler, CancellationToken cancellationToken) =>"
            )
            .B(_ =>
            {
                _.L("ArgumentNullException.ThrowIfNull(httpContext);");
                _.L("ArgumentNullException.ThrowIfNull(handler);");
                _.L();
                if (HasHttpErrorHandlers(model, operation))
                {
                    _.L("try")
                        .B(_ =>
                        {
                            AddAspNetCoreOperationBody(_, model, service, operation, options);
                        });
                    AddAspNetCoreErrorHandlers(_, model, operation, service.Id.Namespace, options);
                }
                else
                {
                    AddAspNetCoreOperationBody(_, model, service, operation, options);
                }
            });
        _.L(");");
    }

    private static void AddAspNetCoreOperationBody(
        ITextBuilder _,
        SmithyModel model,
        ModelShape service,
        ModelShape operation,
        CSharpGenerationOptions options
    )
    {
        var descriptorAccess =
            $"{GetServiceContractName(GetTypeName(service.Id))}Descriptor.{CSharpIdentifier.TypeName(operation.Id.Name)}";
        if (operation.Input is { } inputId)
        {
            var inputShape = model.GetShape(inputId);
            var inputType = GetTypeReference(inputId, service.Id.Namespace, options.BaseNamespace);
            AddAspNetCoreInputBinding(_, model, service, inputShape, inputType, options);
            _.L(
                operation.Output is null
                    ? $"await {descriptorAccess}.InvokeAsync(handler, input, cancellationToken).ConfigureAwait(false);"
                    : $"var output = await {descriptorAccess}.InvokeAsync(handler, input, cancellationToken).ConfigureAwait(false);"
            );
        }
        else
        {
            _.L(
                operation.Output is null
                    ? $"await {descriptorAccess}.InvokeAsync(handler, SmithyUnit.Value, cancellationToken).ConfigureAwait(false);"
                    : $"var output = await {descriptorAccess}.InvokeAsync(handler, SmithyUnit.Value, cancellationToken).ConfigureAwait(false);"
            );
        }

        _.L(
            operation.Output is null
                ? "httpContext.Response.StatusCode = StatusCodes.Status204NoContent;"
                : GetAspNetCoreOutputResponseStatement(model, operation)
        );
    }

    private static string GetAspNetCoreOutputResponseStatement(SmithyModel model, ModelShape operation)
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

    private static void AddAspNetCoreBoundResponseWriters(
        ITextBuilder _,
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
            AddAspNetCoreBoundResponseWriter(_, output, service.Id.Namespace, options);
            _.L();
        }
    }

    private static void AddAspNetCoreBoundResponseWriter(
        ITextBuilder _,
        ModelShape output,
        string currentNamespace,
        CSharpGenerationOptions options
    )
    {
        var outputType = GetTypeReference(output.Id, currentNamespace, options.BaseNamespace);
        _.L(
                $"private static async Task WriteBoundResponseAsync(HttpContext httpContext, {outputType} output, CancellationToken cancellationToken)"
            )
            .B(_ =>
            {
                _.L("ArgumentNullException.ThrowIfNull(output);");
                foreach (var member in GetSortedMembers(output).Where(IsHttpResponseCodeMember))
                {
                    var propertyName = CSharpIdentifier.PropertyName(member.Name);
                    _.L($"SmithyAspNetCoreProtocol.SetStatusCode(httpContext, output.{propertyName});");
                }

                foreach (var member in GetSortedMembers(output).Where(IsHttpHeaderMember))
                {
                    var propertyName = CSharpIdentifier.PropertyName(member.Name);
                    var headerName = member.Traits[SmithyPrelude.HttpHeaderTrait].AsString();
                    _.L($"SmithyAspNetCoreProtocol.AddResponseHeader(httpContext, {FormatString(headerName)}, output.{propertyName});");
                }

                foreach (var member in GetSortedMembers(output).Where(IsHttpPrefixHeadersMember))
                {
                    var propertyName = CSharpIdentifier.PropertyName(member.Name);
                    var headerPrefix = member.Traits[SmithyPrelude.HttpPrefixHeadersTrait].AsString();
                    _.L(
                        $"SmithyAspNetCoreProtocol.AddPrefixedResponseHeaders(httpContext, {FormatString(headerPrefix)}, output.{propertyName});"
                    );
                }

                if (GetSortedMembers(output).FirstOrDefault(IsHttpPayloadMember) is { } payloadMember)
                {
                    var propertyName = CSharpIdentifier.PropertyName(payloadMember.Name);
                    _.L(
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

                _.L($"var responseBody = new {GetBodyProjectionTypeName(output)}(")
                    .B(
                        _ =>
                        {
                            for (var i = 0; i < bodyMembers.Length; i++)
                            {
                                var member = bodyMembers[i];
                                var propertyName = CSharpIdentifier.PropertyName(member.Name);
                                var suffix = i == bodyMembers.Length - 1 ? string.Empty : ",";
                                _.L($"output.{propertyName}{suffix}");
                            }
                        },
                        ConfigureTextBlock(BlockStyle.IndentOnly)
                    );
                _.L(");");
                _.L(
                    "await SmithyAspNetCoreProtocol.WriteJsonResponseAsync(httpContext, responseBody, cancellationToken).ConfigureAwait(false);"
                );
            });
    }

    private static void AddAspNetCoreInputBinding(
        ITextBuilder _,
        SmithyModel model,
        ModelShape service,
        ModelShape input,
        string inputType,
        CSharpGenerationOptions options
    )
    {
        var members = GetConstructorMembers(model, input, options);
        var bodyMembers = GetRequestBodyMembers(model, input, options);
        var hasBody = bodyMembers.Length > 0;
        if (members.Length == 0)
        {
            _.L($"var input = new {inputType}();");
            return;
        }

        string? bodyVariable = null;
        if (hasBody)
        {
            var requiresBody = bodyMembers.Any(member => IsRequiredHttpInputMember(input, member, options));
            var bodyType = GetBodyProjectionTypeName(input);
            _.L(
                $"var body = await {(requiresBody ? $"SmithyAspNetCoreProtocol.ReadRequiredJsonRequestBodyAsync<{bodyType}>(httpContext, cancellationToken)" : $"SmithyAspNetCoreProtocol.ReadJsonRequestBodyAsync<{bodyType}>(httpContext, cancellationToken)")}.ConfigureAwait(false);"
            );
            _.L();
            bodyVariable = "body";
        }

        _.L($"var input = new {inputType}(")
            .B(
                _ =>
                {
                    for (var i = 0; i < members.Length; i++)
                    {
                        var suffix = i == members.Length - 1 ? string.Empty : ",";
                        _.L(
                            $"{GetAspNetCoreInputMemberExpression(model, input, members[i], service.Id.Namespace, options, bodyVariable)}{suffix}"
                        );
                    }
                },
                ConfigureTextBlock(BlockStyle.IndentOnly)
            );
        _.L(");");
    }

    private static string GetAspNetCoreInputMemberExpression(
        SmithyModel model,
        ModelShape input,
        MemberShape member,
        string currentNamespace,
        CSharpGenerationOptions options,
        string? bodyVariable = null
    )
    {
        var memberType = GetMemberParameterType(model, input, member, currentNamespace, options);
        var required = IsRequiredHttpInputMember(input, member, options);
        if (IsHttpLabelMember(member))
        {
            return $"SmithyAspNetCoreProtocol.GetRouteValue<{memberType}>(httpContext, {FormatString(member.Name)})";
        }

        if (IsHttpQueryMember(member))
        {
            var queryName = member.Traits[SmithyPrelude.HttpQueryTrait].AsString();
            return required
                ? $"SmithyAspNetCoreProtocol.GetRequiredQueryValue<{memberType}>(httpContext, {FormatString(queryName)})"
                : $"SmithyAspNetCoreProtocol.GetQueryValue<{memberType}>(httpContext, {FormatString(queryName)})";
        }

        if (IsHttpQueryParamsMember(member))
        {
            var excludedNames = GetSortedMembers(input)
                .Where(IsHttpQueryMember)
                .Select(queryMember => queryMember.Traits[SmithyPrelude.HttpQueryTrait].AsString());
            var expression =
                $"SmithyAspNetCoreProtocol.GetQueryParams<{memberType}>(httpContext, [{string.Join(", ", excludedNames.Select(FormatString))}])";
            return required ? $"{expression}!" : expression;
        }

        if (IsHttpHeaderMember(member))
        {
            var headerName = member.Traits[SmithyPrelude.HttpHeaderTrait].AsString();
            return required
                ? $"SmithyAspNetCoreProtocol.GetRequiredHeaderValue<{memberType}>(httpContext, {FormatString(headerName)})"
                : $"SmithyAspNetCoreProtocol.GetHeaderValue<{memberType}>(httpContext, {FormatString(headerName)})";
        }

        if (IsHttpPrefixHeadersMember(member))
        {
            var headerPrefix = member.Traits[SmithyPrelude.HttpPrefixHeadersTrait].AsString();
            var expression = $"SmithyAspNetCoreProtocol.GetPrefixedHeaders<{memberType}>(httpContext, {FormatString(headerPrefix)})";
            return required ? $"{expression}!" : expression;
        }

        if (IsHttpPayloadMember(member))
        {
            return $"await SmithyAspNetCoreProtocol.ReadJsonRequestBodyAsync<{memberType}>(httpContext, cancellationToken).ConfigureAwait(false)";
        }

        if (bodyVariable is not null)
        {
            return $"{bodyVariable}.{CSharpIdentifier.PropertyName(member.Name)}";
        }

        throw new SmithyException(
            $"HTTP document body member '{member.Name}' on shape '{input.Id}' was requested without a generated body projection."
        );
    }

    private static void AddAspNetCoreBodyProjectionTypes(
        ITextBuilder _,
        SmithyModel model,
        ModelShape service,
        CSharpGenerationOptions options
    )
    {
        var emitted = new HashSet<ShapeId>();
        foreach (var operationId in service.Operations.OrderBy(id => id.ToString(), StringComparer.Ordinal))
        {
            var operation = model.GetShape(operationId);
            if (operation.Input is { } inputId && emitted.Add(inputId))
            {
                var input = model.GetShape(inputId);
                var inputBodyMembers = GetRequestBodyMembers(model, input, options);
                if (inputBodyMembers.Length > 0)
                {
                    _.L();
                    AddBodyProjectionType(_, model, service, input, inputBodyMembers, options);
                }
            }

            if (operation.Output is { } outputId && emitted.Add(outputId))
            {
                var output = model.GetShape(outputId);
                if (HasResponseBindings(output))
                {
                    var outputBodyMembers = GetResponseBodyMembers(model, output, options);
                    if (outputBodyMembers.Length > 0)
                    {
                        _.L();
                        AddBodyProjectionType(_, model, service, output, outputBodyMembers, options);
                    }
                }
            }
        }
    }

    private static bool IsRequiredHttpInputMember(ModelShape container, MemberShape member, CSharpGenerationOptions options)
    {
        if (IsHttpLabelMember(member))
        {
            return true;
        }

        return member.IsRequired && GetEffectiveDefaultValue(container, member, options) is null;
    }

    private static void AddAspNetCoreErrorHandlers(
        ITextBuilder _,
        SmithyModel model,
        ModelShape operation,
        string currentNamespace,
        CSharpGenerationOptions options
    )
    {
        foreach (var errorId in operation.Errors.OrderBy(id => id.ToString(), StringComparer.Ordinal))
        {
            var error = model.GetShape(errorId);
            if (GetHttpErrorCode(error) is not { } statusCode)
            {
                continue;
            }

            var errorType = GetTypeReference(errorId, currentNamespace, options.BaseNamespace);
            _.L($"catch ({errorType} error)")
                .B(_ =>
                {
                    _.L($"httpContext.Response.StatusCode = {statusCode.ToString(CultureInfo.InvariantCulture)};");
                    _.L(
                        "await SmithyAspNetCoreProtocol.WriteJsonResponseAsync(httpContext, error, cancellationToken).ConfigureAwait(false);"
                    );
                });
        }
    }

    private static bool HasHttpErrorHandlers(SmithyModel model, ModelShape operation)
    {
        return operation.Errors.Any(errorId => GetHttpErrorCode(model.GetShape(errorId)) is not null);
    }

    private static string GetServerOperationSignature(ModelShape service, ModelShape operation, CSharpGenerationOptions options)
    {
        var methodName = $"{CSharpIdentifier.PropertyName(operation.Id.Name)}Async";
        var inputType = operation.Input is { } input ? GetTypeReference(input, service.Id.Namespace, options.BaseNamespace) : null;
        var outputType = operation.Output is { } output ? GetTypeReference(output, service.Id.Namespace, options.BaseNamespace) : null;
        var returnType = outputType is null ? "Task" : $"Task<{outputType}>";
        var parameters = inputType is null
            ? "CancellationToken cancellationToken = default"
            : $"{inputType} input, CancellationToken cancellationToken = default";
        return $"{returnType} {methodName}({parameters})";
    }

    private static void AddGrpcEndpointExtensions(ITextBuilder _, string serviceContractName)
    {
        _.L($"public static class {serviceContractName}GrpcExtensions")
            .B(_ =>
            {
                _.L($"public static IEndpointRouteBuilder Map{serviceContractName}Grpc(this IEndpointRouteBuilder endpoints)")
                    .B(_ =>
                    {
                        _.L("ArgumentNullException.ThrowIfNull(endpoints);");
                        _.L();
                        _.L($"endpoints.MapGrpcService<{serviceContractName}GrpcAdapter>();");
                        _.L("return endpoints;");
                    });
            });
    }

    private static void AddGrpcAdapter(
        ITextBuilder _,
        SmithyModel model,
        ModelShape service,
        string serviceTypeName,
        string serviceContractName,
        string interfaceName,
        CSharpGenerationOptions options
    )
    {
        var grpcNamespace = GetGrpcNamespace(service, options);
        var grpcServiceBaseType = $"global::{grpcNamespace}.{serviceTypeName}.{serviceTypeName}Base";
        var adapterName = $"{serviceContractName}GrpcAdapter";

        _.L($"public sealed class {adapterName} : {grpcServiceBaseType}")
            .B(_ =>
            {
                _.L($"private readonly {interfaceName} _handler;");
                _.L();
                _.L($"public {adapterName}({interfaceName} handler)")
                    .B(_ =>
                    {
                        _.L("_handler = handler ?? throw new ArgumentNullException(nameof(handler));");
                    });

                foreach (var operationId in service.Operations.OrderBy(id => id.ToString(), StringComparer.Ordinal))
                {
                    _.L();
                    AddGrpcAdapterMethod(_, model, service, model.GetShape(operationId), serviceTypeName, serviceContractName, options);
                }
            });
    }

    private static void AddGrpcAdapterMethod(
        ITextBuilder _,
        SmithyModel model,
        ModelShape service,
        ModelShape operation,
        string serviceTypeName,
        string serviceContractName,
        CSharpGenerationOptions options
    )
    {
        var operationName = CSharpIdentifier.PropertyName(operation.Id.Name);
        var grpcNamespace = GetGrpcNamespace(service, options);
        var grpcInputType = GetGrpcOperationMessageType(operation.Input, grpcNamespace);
        var grpcOutputType = GetGrpcOperationMessageType(operation.Output, grpcNamespace);
        var descriptorAccess = $"{serviceContractName}Descriptor.{CSharpIdentifier.TypeName(operation.Id.Name)}";
        var smithyInputExpression = operation.Input is { } inputId
            ? GetGrpcToSmithyValueExpression(model, inputId, "request", service.Id.Namespace, options)
            : "SmithyUnit.Value";

        _.L($"public override async Task<{grpcOutputType}> {operationName}(")
            .B(
                _ =>
                {
                    _.L($"{grpcInputType} request,");
                    _.L("ServerCallContext context");
                },
                ConfigureTextBlock(BlockStyle.IndentOnly)
            );
        _.L(")")
            .B(_ =>
            {
                if (operation.Output is { } outputId)
                {
                    _.L(
                        $"var output = await {descriptorAccess}.InvokeAsync(_handler, {smithyInputExpression}, context.CancellationToken).ConfigureAwait(false);"
                    );
                    _.L($"return {GetSmithyToGrpcValueExpression(model, outputId, "output", service.Id.Namespace, options)};");
                }
                else
                {
                    _.L(
                        $"await {descriptorAccess}.InvokeAsync(_handler, {smithyInputExpression}, context.CancellationToken).ConfigureAwait(false);"
                    );
                    _.L("return new Empty();");
                }
            });
    }

    private static string GetGrpcNamespace(ModelShape service, CSharpGenerationOptions options)
    {
        return $"{CSharpIdentifier.Namespace(service.Id.Namespace, options.BaseNamespace)}.Grpc";
    }

    private static string GetGrpcOperationMessageType(ShapeId? shapeId, string grpcNamespace)
    {
        if (shapeId is not { } value)
        {
            return "global::Google.Protobuf.WellKnownTypes.Empty";
        }

        return $"global::{grpcNamespace}.{GetTypeName(value)}";
    }

    private static string GetGrpcToSmithyValueExpression(
        SmithyModel model,
        ShapeId target,
        string sourceExpression,
        string currentNamespace,
        CSharpGenerationOptions options
    )
    {
        if (target.Namespace == SmithyPrelude.Namespace)
        {
            return target.Name switch
            {
                "Blob" => $"{sourceExpression}.ToByteArray()",
                "Boolean" or "Byte" or "Short" or "Integer" or "Long" or "Float" or "Double" or "String" => sourceExpression,
                _ => throw new SmithyException($"gRPC server generation does not support Smithy target '{target}' yet."),
            };
        }

        var shape = model.GetShape(target);
        return shape.Kind switch
        {
            ShapeKind.Structure => GetGrpcToSmithyStructureExpression(model, shape, sourceExpression, currentNamespace, options),
            ShapeKind.IntEnum => $"({GetTypeReference(target, currentNamespace, options.BaseNamespace)}){sourceExpression}",
            _ => throw new SmithyException($"gRPC server generation does not support shape '{target}' of kind '{shape.Kind}' yet."),
        };
    }

    private static string GetGrpcToSmithyStructureExpression(
        SmithyModel model,
        ModelShape shape,
        string sourceExpression,
        string currentNamespace,
        CSharpGenerationOptions options
    )
    {
        var typeName = GetTypeReference(shape.Id, currentNamespace, options.BaseNamespace);
        var members = GetConstructorMembers(model, shape, options);
        if (members.Length == 0)
        {
            return $"new {typeName}()";
        }

        return $"new {typeName}({string.Join(", ", members.Select(member => GetGrpcToSmithyConstructorArgument(model, shape, member, sourceExpression, currentNamespace, options)))})";
    }

    private static string GetSmithyToGrpcValueExpression(
        SmithyModel model,
        ShapeId target,
        string sourceExpression,
        string currentNamespace,
        CSharpGenerationOptions options
    )
    {
        if (target.Namespace == SmithyPrelude.Namespace)
        {
            return target.Name switch
            {
                "Blob" => $"global::Google.Protobuf.ByteString.CopyFrom({sourceExpression} ?? Array.Empty<byte>())",
                "Boolean" or "Byte" or "Short" or "Integer" or "Long" or "Float" or "Double" or "String" => sourceExpression,
                _ => throw new SmithyException($"gRPC server generation does not support Smithy target '{target}' yet."),
            };
        }

        var shape = model.GetShape(target);
        return shape.Kind switch
        {
            ShapeKind.Structure => GetSmithyToGrpcStructureExpression(model, shape, sourceExpression, currentNamespace, options),
            ShapeKind.IntEnum => $"(global::{GetGrpcNamespace(shape, options)}.{GetTypeName(shape.Id)}){sourceExpression}",
            _ => throw new SmithyException($"gRPC server generation does not support shape '{target}' of kind '{shape.Kind}' yet."),
        };
    }

    private static string GetSmithyToGrpcStructureExpression(
        SmithyModel model,
        ModelShape shape,
        string sourceExpression,
        string currentNamespace,
        CSharpGenerationOptions options
    )
    {
        var grpcType = $"global::{GetGrpcNamespace(shape, options)}.{GetTypeName(shape.Id)}";
        var members = GetSortedMembers(shape).ToArray();
        if (members.Length == 0)
        {
            return $"new {grpcType}()";
        }

        var lines = new List<string> { $"var message = new {grpcType}();" };
        foreach (var member in members)
        {
            lines.AddRange(GetSmithyToGrpcMemberAssignments(model, shape, member, sourceExpression, currentNamespace, options));
        }

        lines.Add("return message;");
        return $"new Func<{grpcType}>(() => {{ {string.Join(" ", lines)} }})()";
    }

    private static string GetGrpcToSmithyConstructorArgument(
        SmithyModel model,
        ModelShape container,
        MemberShape member,
        string sourceExpression,
        string currentNamespace,
        CSharpGenerationOptions options
    )
    {
        var propertyName = CSharpIdentifier.PropertyName(member.Name);
        var memberAccess = $"{sourceExpression}.{propertyName}";
        if (!HasGrpcPresenceSensitiveConstructorParameter(container, member, model, options))
        {
            return GetGrpcToSmithyValueExpression(model, member.Target, memberAccess, currentNamespace, options);
        }

        if (SupportsProto3OptionalPresence(model, member.Target))
        {
            return $"{sourceExpression}.Has{propertyName} ? {GetGrpcToSmithyValueExpression(model, member.Target, memberAccess, currentNamespace, options)} : null";
        }

        var targetShape = model.GetShape(member.Target);
        if (targetShape.Kind == ShapeKind.Structure)
        {
            return $"{memberAccess} is null ? null : {GetGrpcToSmithyValueExpression(model, member.Target, memberAccess, currentNamespace, options)}";
        }

        return GetGrpcToSmithyValueExpression(model, member.Target, memberAccess, currentNamespace, options);
    }

    private static IEnumerable<string> GetSmithyToGrpcMemberAssignments(
        SmithyModel model,
        ModelShape container,
        MemberShape member,
        string sourceExpression,
        string currentNamespace,
        CSharpGenerationOptions options
    )
    {
        var propertyName = CSharpIdentifier.PropertyName(member.Name);
        var memberAccess = $"{sourceExpression}.{propertyName}";
        var assignment =
            $"message.{propertyName} = {GetSmithyToGrpcValueExpression(model, member.Target, memberAccess, currentNamespace, options)};";
        if (!IsNullableMember(container, member, options))
        {
            return [assignment];
        }

        return [$"if ({memberAccess} is not null)", "{", assignment, "}"];
    }

    private static bool HasGrpcPresenceSensitiveConstructorParameter(
        ModelShape container,
        MemberShape member,
        SmithyModel model,
        CSharpGenerationOptions options
    )
    {
        _ = model;
        return IsNullableMember(container, member, options) || GetEffectiveDefaultValue(container, member, options) is not null;
    }

    private static bool SupportsProto3OptionalPresence(SmithyModel model, ShapeId target)
    {
        if (target.Namespace == SmithyPrelude.Namespace)
        {
            return target.Name is "String" or "Boolean" or "Blob" or "Byte" or "Short" or "Integer" or "Long" or "Float" or "Double";
        }

        var shape = model.GetShape(target);
        return shape.Kind is ShapeKind.Enum or ShapeKind.IntEnum;
    }
}

#pragma warning restore CA1305
