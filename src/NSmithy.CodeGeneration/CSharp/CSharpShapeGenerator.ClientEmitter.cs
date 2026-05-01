using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Nest.Text;
using NSmithy.CodeGeneration.Model;
using NSmithy.Core;
using NSmithy.Core.Traits;

namespace NSmithy.CodeGeneration.CSharp;

#pragma warning disable CA1305 // Source text is assembled from Smithy identifiers and explicitly formatted literals.

public sealed partial class CSharpShapeGenerator
{
    private static GeneratedCSharpFile GenerateClient(SmithyModel model, ModelShape service, CSharpGenerationOptions options)
    {
        var emitsHttp = EmitsHttpClient(service);
        var emitsGrpc = EmitsGrpcClient(service);
        var extraUsings = new List<string> { "System.Threading", "System.Threading.Tasks" };
        if (emitsHttp)
        {
            extraUsings.AddRange([
                "System.Collections",
                "System.Globalization",
                "System.Net",
                "System.Net.Http",
                "System.Text",
                "System.Xml.Linq",
                "NSmithy.Client",
                "NSmithy.Http",
                "NSmithy.Codecs",
            ]);

            if (IsRestXmlService(service))
            {
                extraUsings.Add("NSmithy.Client.RestXml");
                extraUsings.Add("NSmithy.Codecs.Xml");
            }
            else if (IsRpcV2CborService(service))
            {
                extraUsings.Add("NSmithy.Client.RpcV2Cbor");
                extraUsings.Add("NSmithy.Codecs.Cbor");
            }
            else
            {
                extraUsings.Add("NSmithy.Client.RestJson");
                extraUsings.Add("NSmithy.Codecs.Json");
            }
        }

        if (emitsGrpc)
        {
            extraUsings.Add("Grpc.Core");
        }

        var _ = CreateTextFileBuilder(service, options, extraUsings.Distinct(StringComparer.Ordinal).ToArray());
        var typeName = $"{GetTypeName(service.Id)}Client";
        var interfaceName = $"I{typeName}";
        _.L($"public interface {interfaceName}")
            .B(_ =>
            {
                foreach (var operationId in service.Operations.OrderBy(id => id.ToString(), StringComparer.Ordinal))
                {
                    var operation = model.GetShape(operationId);
                    _.L($"{GetOperationSignature(model, service, operation, options)};");
                }
            });
        _.L();

        if (emitsHttp)
        {
            _.L($"public sealed class {typeName} : {interfaceName}")
                .B(_ =>
                {
                    _.L($"private static readonly ISmithyPayloadCodec DocumentCodec = {GetDocumentCodecExpression(service)};");
                    _.L("private readonly SmithyOperationInvoker invoker;");
                    _.L();
                    _.L($"public {typeName}(Uri endpoint)")
                        .B(
                            _ =>
                            {
                                _.L(": this(new HttpClient(), new SmithyClientOptions { Endpoint = endpoint })");
                            },
                            ConfigureTextBlock(BlockStyle.IndentOnly)
                        );
                    _.L("{");
                    _.L("}");
                    _.L();
                    _.L($"public {typeName}(HttpClient httpClient)")
                        .B(
                            _ =>
                            {
                                _.L(": this(httpClient, SmithyClientOptions.Default)");
                            },
                            ConfigureTextBlock(BlockStyle.IndentOnly)
                        );
                    _.L("{");
                    _.L("}");
                    _.L();
                    _.L($"public {typeName}(HttpClient httpClient, SmithyClientOptions options)")
                        .B(
                            _ =>
                            {
                                _.L(
                                    ": this(new SmithyOperationInvoker(new HttpClientTransport(httpClient, (options ?? throw new ArgumentNullException(nameof(options))).Endpoint), options.Middleware))"
                                );
                            },
                            ConfigureTextBlock(BlockStyle.IndentOnly)
                        );
                    _.L("{");
                    _.L("}");
                    _.L();
                    _.L($"public {typeName}(SmithyOperationInvoker invoker)")
                        .B(_ =>
                        {
                            _.L("this.invoker = invoker ?? throw new ArgumentNullException(nameof(invoker));");
                        });
                    _.L();

                    foreach (var operationId in service.Operations.OrderBy(id => id.ToString(), StringComparer.Ordinal))
                    {
                        AddHttpOperationMethod(_, model, service, operationId, options);
                    }

                    foreach (var operationId in service.Operations.OrderBy(id => id.ToString(), StringComparer.Ordinal))
                    {
                        AddErrorDeserializer(_, model, operationId, options);
                    }

                    AddHttpBodyProjectionTypes(_, model, service, options);
                });
            _.L();
        }

        if (emitsGrpc)
        {
            AddGrpcClient(_, model, service, interfaceName, options);
        }

        return new GeneratedCSharpFile(GetClientPath(service), FormatGeneratedText(_));
    }

    private static void AddHttpOperationMethod(
        ITextBuilder _,
        SmithyModel model,
        ModelShape service,
        ShapeId operationId,
        CSharpGenerationOptions options
    )
    {
        var operation = model.GetShape(operationId);
        var protocolRuntime = GetClientProtocolRuntimeType(service);
        var isRpcV2Cbor = IsRpcV2CborService(service);
        var isRestXml = IsRestXmlService(service);
        var httpBinding = isRpcV2Cbor
            ? new HttpBinding("POST", $"/service/{service.Id.Name}/operation/{operation.Id.Name}")
            : ReadHttpBinding(operation);
        if (
            !isRpcV2Cbor
            && service.Traits.Has(SmithyPrelude.SimpleRestJsonTrait)
            && httpBinding.Uri.Length > 1
            && httpBinding.Uri.EndsWith('/')
        )
        {
            httpBinding = httpBinding with { Uri = httpBinding.Uri.TrimEnd('/') };
        }

        var inputShape = operation.Input is { } operationInput ? model.GetShape(operationInput) : null;
        ModelShape? outputShape = null;
        string? outputType = null;
        if (operation.Output is { } operationOutput)
        {
            outputShape = model.GetShape(operationOutput);
            outputType = GetTypeReference(operationOutput, service.Id.Namespace, options.BaseNamespace);
        }

        _.L($"public async {GetOperationSignature(model, service, operation, options)}")
            .B(_ =>
            {
                if (operation.Input is not null)
                {
                    _.L("ArgumentNullException.ThrowIfNull(input);");
                }

                if (isRpcV2Cbor)
                {
                    _.L($"var requestUri = {FormatString(httpBinding.Uri)};");
                }
                else
                {
                    AddRequestUriBuilder(_, model, service, inputShape, httpBinding);
                }

                _.L($"var request = new SmithyHttpRequest(new HttpMethod({FormatString(httpBinding.Method)}), requestUri);");
                if (isRpcV2Cbor)
                {
                    _.L("request.Headers[\"Smithy-Protocol\"] = [\"rpc-v2-cbor\"];");
                    _.L("request.Headers[\"Accept\"] = [DocumentCodec.MediaType];");
                }
                else if (inputShape is not null)
                {
                    AddRequestHeaders(_, service, inputShape);
                }

                if (isRpcV2Cbor)
                {
                    if (operation.Input is not null)
                    {
                        _.L("request.Content = DocumentCodec.Serialize(input);");
                        _.L("request.ContentType = DocumentCodec.MediaType;");
                    }
                }
                else if (inputShape is not null && TryGetPayloadMember(inputShape, out var payloadMember))
                {
                    var propertyName = CSharpIdentifier.PropertyName(payloadMember.Name);
                    if (GetEffectiveDefaultValue(inputShape, payloadMember, options) is { } defaultValue)
                    {
                        var memberType = GetMemberType(model, inputShape, payloadMember, service.Id.Namespace, options);
                        var defaultExpression = GetDefaultExpression(
                            model,
                            payloadMember.Target,
                            defaultValue,
                            service.Id.Namespace,
                            options.BaseNamespace
                        );
                        _.L($"if (!EqualityComparer<{memberType}>.Default.Equals(input.{propertyName}, {defaultExpression}))")
                            .B(_ =>
                            {
                                _.L($"request.Content = DocumentCodec.Serialize(input.{propertyName});");
                                _.L("request.ContentType = DocumentCodec.MediaType;");
                            });
                    }
                    else
                    {
                        _.L($"request.Content = DocumentCodec.Serialize(input.{propertyName});");
                        _.L("request.ContentType = DocumentCodec.MediaType;");
                    }
                }
                else if (inputShape is not null && HasHttpBody(inputShape))
                {
                    AddRequestBody(_, inputShape, service);
                }

                _.L();
                _.L(
                    $"var response = await invoker.InvokeAsync({FormatString(service.Id.Name)}, {FormatString(operation.Id.Name)}, request, Deserialize{CSharpIdentifier.TypeName(operation.Id.Name)}ErrorAsync, cancellationToken).ConfigureAwait(false);"
                );

                if (outputShape is null || outputType is null)
                {
                    _.L("return;");
                    return;
                }

                _.L();
                if (isRpcV2Cbor)
                {
                    _.L($"{protocolRuntime}.EnsureResponse(response);");
                    _.L("if (response.StatusCode != HttpStatusCode.OK)")
                        .B(_ =>
                        {
                            _.L("throw new SmithyClientException(response.StatusCode, response.ReasonPhrase);");
                        });
                    _.L();
                }
                AddResponseReturn(_, model, service, outputShape, outputType, service.Id.Namespace, options, isRpcV2Cbor || isRestXml);
            });
        _.L();
    }

    private static void AddGrpcClient(
        ITextBuilder _,
        SmithyModel model,
        ModelShape service,
        string interfaceName,
        CSharpGenerationOptions options
    )
    {
        var serviceTypeName = GetTypeName(service.Id);
        var grpcNamespace = GetGrpcNamespace(service, options);
        var rawClientType = $"global::{grpcNamespace}.{serviceTypeName}.{serviceTypeName}Client";
        var grpcClientTypeName = $"{serviceTypeName}GrpcClient";

        _.L($"public sealed class {grpcClientTypeName} : {interfaceName}")
            .B(_ =>
            {
                _.L($"private readonly {rawClientType} client;");
                _.L();
                _.L($"public {grpcClientTypeName}(ChannelBase channel)")
                    .B(
                        _ =>
                        {
                            _.L($": this(new {rawClientType}(channel ?? throw new ArgumentNullException(nameof(channel))))");
                        },
                        ConfigureTextBlock(BlockStyle.IndentOnly)
                    );
                _.L("{");
                _.L("}");
                _.L();
                _.L($"public {grpcClientTypeName}(CallInvoker callInvoker)")
                    .B(
                        _ =>
                        {
                            _.L($": this(new {rawClientType}(callInvoker ?? throw new ArgumentNullException(nameof(callInvoker))))");
                        },
                        ConfigureTextBlock(BlockStyle.IndentOnly)
                    );
                _.L("{");
                _.L("}");
                _.L();
                _.L($"public {grpcClientTypeName}({rawClientType} client)")
                    .B(_ =>
                    {
                        _.L("this.client = client ?? throw new ArgumentNullException(nameof(client));");
                    });
                _.L();

                foreach (var operationId in service.Operations.OrderBy(id => id.ToString(), StringComparer.Ordinal))
                {
                    AddGrpcOperationMethod(_, model, service, model.GetShape(operationId), options);
                    _.L();
                }
            });
    }

    private static void AddGrpcOperationMethod(
        ITextBuilder _,
        SmithyModel model,
        ModelShape service,
        ModelShape operation,
        CSharpGenerationOptions options
    )
    {
        var operationName = CSharpIdentifier.PropertyName(operation.Id.Name);
        var grpcNamespace = GetGrpcNamespace(service, options);
        var grpcInputType = GetGrpcOperationMessageType(operation.Input, grpcNamespace);
        var grpcInputExpression = operation.Input is { } inputId
            ? GetSmithyToGrpcValueExpression(model, inputId, "input", service.Id.Namespace, options)
            : "new global::Google.Protobuf.WellKnownTypes.Empty()";

        _.L($"public async {GetOperationSignature(model, service, operation, options)}")
            .B(_ =>
            {
                if (operation.Input is not null)
                {
                    _.L("ArgumentNullException.ThrowIfNull(input);");
                    _.L();
                }

                _.L($"{grpcInputType} request = {grpcInputExpression};");
                if (operation.Output is { } outputId)
                {
                    _.L(
                        $"var response = await client.{operationName}Async(request, cancellationToken: cancellationToken).ResponseAsync.ConfigureAwait(false);"
                    );
                    _.L($"return {GetGrpcToSmithyValueExpression(model, outputId, "response", service.Id.Namespace, options)};");
                }
                else
                {
                    _.L(
                        $"await client.{operationName}Async(request, cancellationToken: cancellationToken).ResponseAsync.ConfigureAwait(false);"
                    );
                }
            });
    }

    private static void AddResponseReturn(
        ITextBuilder _,
        SmithyModel model,
        ModelShape service,
        ModelShape output,
        string outputType,
        string currentNamespace,
        CSharpGenerationOptions options,
        bool useDocumentBindings
    )
    {
        var protocolRuntime = GetClientProtocolRuntimeType(service);
        if (useDocumentBindings || !HasResponseBindings(output))
        {
            _.L($"return {protocolRuntime}.DeserializeRequiredBody<{outputType}>(DocumentCodec, response.Content);");
            return;
        }

        var bodyMembers = GetResponseBodyMembers(model, output, options);
        string? bodyVariable = null;
        if (bodyMembers.Length > 0)
        {
            var bodyType = GetBodyProjectionTypeName(output);
            var requiresBody = bodyMembers.Any(member => IsRequiredHttpOutputMember(output, member, options));
            _.L(
                $"var body = {(requiresBody ? $"{protocolRuntime}.DeserializeRequiredBody<{bodyType}>(DocumentCodec, response.Content)" : $"{protocolRuntime}.DeserializeBody<{bodyType}>(DocumentCodec, response.Content)")};"
            );
            _.L();
            bodyVariable = "body";
        }

        _.L($"return new {outputType}(")
            .B(
                builder =>
                {
                    var members = GetConstructorMembers(model, output, options);
                    for (var i = 0; i < members.Length; i++)
                    {
                        var suffix = i == members.Length - 1 ? string.Empty : ",";
                        builder.L(
                            $"{GetResponseMemberExpression(model, service, output, members[i], currentNamespace, options, bodyVariable: bodyVariable)}{suffix}"
                        );
                    }
                },
                ConfigureTextBlock(BlockStyle.IndentOnly)
            );
        _.L(");");
    }

    private static string GetResponseMemberExpression(
        SmithyModel model,
        ModelShape service,
        ModelShape output,
        MemberShape member,
        string currentNamespace,
        CSharpGenerationOptions options,
        bool isError = false,
        string? bodyVariable = null
    )
    {
        _ = isError;
        var protocolRuntime = GetClientProtocolRuntimeType(service);
        var memberType = GetMemberParameterType(model, output, member, currentNamespace, options);
        var required = IsRequiredHttpOutputMember(output, member, options);
        if (IsHttpHeaderMember(member))
        {
            var headerName = member.Traits[SmithyPrelude.HttpHeaderTrait].AsString();
            return required
                ? $"{protocolRuntime}.GetRequiredHeader<{memberType}>(response.Headers, {FormatString(headerName)})"
                : $"{protocolRuntime}.GetHeader<{memberType}>(response.Headers, {FormatString(headerName)})";
        }

        if (IsHttpPrefixHeadersMember(member))
        {
            var headerPrefix = member.Traits[SmithyPrelude.HttpPrefixHeadersTrait].AsString();
            return required
                ? $"{protocolRuntime}.GetRequiredPrefixedHeaders<{memberType}>(response.Headers, {FormatString(headerPrefix)})"
                : $"{protocolRuntime}.GetPrefixedHeaders<{memberType}>(response.Headers, {FormatString(headerPrefix)})";
        }

        if (IsHttpResponseCodeMember(member))
        {
            return $"({memberType})(int)response.StatusCode";
        }

        if (IsHttpPayloadMember(member))
        {
            return required
                ? $"{protocolRuntime}.DeserializeRequiredBody<{memberType}>(DocumentCodec, response.Content)"
                : $"{protocolRuntime}.DeserializeBody<{memberType}>(DocumentCodec, response.Content)";
        }

        var memberName = GetDocumentMemberName(member, service);
        if (bodyVariable is not null)
        {
            return $"{bodyVariable}.{CSharpIdentifier.PropertyName(member.Name)}";
        }

        throw new SmithyException(
            $"HTTP document body member '{memberName}' on shape '{output.Id}' was requested without a generated body projection."
        );
    }

    private static bool IsRequiredHttpOutputMember(ModelShape container, MemberShape member, CSharpGenerationOptions options)
    {
        return member.IsRequired && GetEffectiveDefaultValue(container, member, options) is null;
    }

    private static void AddRequestUriBuilder(
        ITextBuilder _,
        SmithyModel model,
        ModelShape service,
        ModelShape? input,
        HttpBinding httpBinding
    )
    {
        var protocolRuntime = GetClientProtocolRuntimeType(service);
        _.L($"var requestUriBuilder = new StringBuilder({FormatString(httpBinding.Uri)});");
        if (input is not null)
        {
            foreach (var member in GetSortedMembers(input).Where(IsHttpLabelMember))
            {
                var propertyName = CSharpIdentifier.PropertyName(member.Name);
                var labelVariableName = $"{CSharpIdentifier.ParameterName(member.Name)}Label";
                var labelExpression = IsReferenceType(model, member.Target)
                    ? $"input.{propertyName} ?? throw new ArgumentException({FormatString($"HTTP label '{member.Name}' is required.")}, nameof(input))"
                    : $"input.{propertyName}";
                _.L($"var {labelVariableName} = {labelExpression};");
                _.L(
                    $"requestUriBuilder.Replace({FormatString($"{{{member.Name}+}}")}, {protocolRuntime}.EscapeGreedyLabel({labelVariableName}));"
                );
                _.L(
                    $"requestUriBuilder.Replace({FormatString($"{{{member.Name}}}")}, Uri.EscapeDataString({protocolRuntime}.FormatHttpValue({labelVariableName})));"
                );
            }

            foreach (var member in GetSortedMembers(input).Where(IsHttpQueryMember))
            {
                var queryName = member.Traits[SmithyPrelude.HttpQueryTrait].AsString();
                var propertyName = CSharpIdentifier.PropertyName(member.Name);
                _.L($"{protocolRuntime}.AppendQuery(requestUriBuilder, {FormatString(queryName)}, input.{propertyName});");
            }

            foreach (var member in GetSortedMembers(input).Where(IsHttpQueryParamsMember))
            {
                var propertyName = CSharpIdentifier.PropertyName(member.Name);
                _.L($"{protocolRuntime}.AppendQueryMap(requestUriBuilder, input.{propertyName});");
            }
        }

        _.L("var requestUri = requestUriBuilder.ToString();");
    }

    private static void AddRequestHeaders(ITextBuilder _, ModelShape service, ModelShape input)
    {
        var protocolRuntime = GetClientProtocolRuntimeType(service);
        foreach (var member in GetSortedMembers(input).Where(IsHttpHeaderMember))
        {
            var headerName = member.Traits[SmithyPrelude.HttpHeaderTrait].AsString();
            var propertyName = CSharpIdentifier.PropertyName(member.Name);
            _.L($"{protocolRuntime}.AddHeader(request.Headers, {FormatString(headerName)}, input.{propertyName});");
        }

        foreach (var member in GetSortedMembers(input).Where(IsHttpPrefixHeadersMember))
        {
            var headerPrefix = member.Traits[SmithyPrelude.HttpPrefixHeadersTrait].AsString();
            var propertyName = CSharpIdentifier.PropertyName(member.Name);
            _.L($"{protocolRuntime}.AddPrefixedHeaders(request.Headers, {FormatString(headerPrefix)}, input.{propertyName});");
        }
    }

    private static void AddRequestBody(ITextBuilder builder, ModelShape input, ModelShape service)
    {
        _ = service.Id;
        var bodyMembers = GetSortedMembers(input).Where(IsHttpBodyMember).ToArray();
        if (bodyMembers.Length == 0)
        {
            return;
        }

        var bodyType = GetBodyProjectionTypeName(input);
        builder
            .L($"var requestBody = new {bodyType}(")
            .B(
                _ =>
                {
                    for (var i = 0; i < bodyMembers.Length; i++)
                    {
                        var member = bodyMembers[i];
                        var propertyName = CSharpIdentifier.PropertyName(member.Name);
                        var suffix = i == bodyMembers.Length - 1 ? string.Empty : ",";
                        _.L($"input.{propertyName}{suffix}");
                    }
                },
                ConfigureTextBlock(BlockStyle.IndentOnly)
            );
        builder.L(");");
        builder.L("request.Content = DocumentCodec.Serialize(requestBody);");
        builder.L("request.ContentType = DocumentCodec.MediaType;");
    }

    private static bool IsHttpBodyMember(MemberShape member)
    {
        return !IsHttpLabelMember(member)
            && !IsHttpQueryMember(member)
            && !IsHttpQueryParamsMember(member)
            && !IsHttpHeaderMember(member)
            && !IsHttpPrefixHeadersMember(member)
            && !IsHttpPayloadMember(member);
    }

    private static bool IsHttpHeaderMember(MemberShape member) => member.Traits.Has(SmithyPrelude.HttpHeaderTrait);

    private static bool IsHttpLabelMember(MemberShape member) => member.Traits.Has(SmithyPrelude.HttpLabelTrait);

    private static bool IsHttpPayloadMember(MemberShape member) => member.Traits.Has(SmithyPrelude.HttpPayloadTrait);

    private static bool IsHttpPrefixHeadersMember(MemberShape member) => member.Traits.Has(SmithyPrelude.HttpPrefixHeadersTrait);

    private static bool IsHttpQueryMember(MemberShape member) => member.Traits.Has(SmithyPrelude.HttpQueryTrait);

    private static bool IsHttpQueryParamsMember(MemberShape member) => member.Traits.Has(SmithyPrelude.HttpQueryParamsTrait);

    private static bool IsHttpResponseCodeMember(MemberShape member) => member.Traits.Has(SmithyPrelude.HttpResponseCodeTrait);

    private static bool HasResponseBindings(ModelShape output)
    {
        return output.Members.Values.Any(member =>
            IsHttpHeaderMember(member)
            || IsHttpPrefixHeadersMember(member)
            || IsHttpPayloadMember(member)
            || IsHttpResponseCodeMember(member)
        );
    }

    private static bool TryGetPayloadMember(ModelShape input, [NotNullWhen(true)] out MemberShape? payloadMember)
    {
        var payloadMembers = input.Members.Values.Where(IsHttpPayloadMember).ToArray();
        if (payloadMembers.Length > 1)
        {
            throw new SmithyException($"Input shape '{input.Id}' has multiple @httpPayload members.");
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
        var inputType = operation.Input is { } input ? GetTypeReference(input, service.Id.Namespace, options.BaseNamespace) : null;
        var outputType = operation.Output is { } output ? GetTypeReference(output, service.Id.Namespace, options.BaseNamespace) : null;
        var returnType = outputType is null ? "Task" : $"Task<{outputType}>";
        var parameters = inputType is null
            ? "CancellationToken cancellationToken = default"
            : $"{inputType} input, CancellationToken cancellationToken = default";
        return $"{returnType} {methodName}({parameters})";
    }

    private static void AddErrorDeserializer(ITextBuilder _, SmithyModel model, ShapeId operationId, CSharpGenerationOptions options)
    {
        var operation = model.GetShape(operationId);
        var service = model.Shapes.Values.First(shape => shape.Kind == ShapeKind.Service && shape.Operations.Contains(operationId));
        var protocolRuntime = GetClientProtocolRuntimeType(service);
        var isRpcV2Cbor = IsRpcV2CborService(service);
        var methodName = $"Deserialize{CSharpIdentifier.TypeName(operation.Id.Name)}ErrorAsync";
        _.L($"private static ValueTask<Exception?> {methodName}(SmithyHttpResponse response, CancellationToken cancellationToken)")
            .B(_ =>
            {
                _.L("if (response.Content.Length == 0)")
                    .B(_ =>
                    {
                        _.L("return ValueTask.FromResult<Exception?>(null);");
                    });

                if (operation.Errors.Count == 0)
                {
                    _.L();
                    _.L("return ValueTask.FromResult<Exception?>(null);");
                    return;
                }

                if (isRpcV2Cbor)
                {
                    _.L($"if (!{protocolRuntime}.HasResponse(response))")
                        .B(_ =>
                        {
                            _.L("return ValueTask.FromResult<Exception?>(null);");
                        });
                    _.L();
                    _.L($"var errorType = {protocolRuntime}.DeserializeErrorType(response.Content);");
                    foreach (var errorId in operation.Errors.OrderBy(id => id.ToString(), StringComparer.Ordinal))
                    {
                        var error = model.GetShape(errorId);
                        _.L(
                                $"if (string.Equals(errorType, {FormatString(errorId.Name)}, StringComparison.Ordinal) || string.Equals(errorType, {FormatString(errorId.ToString())}, StringComparison.Ordinal))"
                            )
                            .B(_ =>
                            {
                                _.L(
                                    $"return ValueTask.FromResult<Exception?>({GetErrorConstructionExpression(model, service, error, errorId, operation.Id.Namespace, options)});"
                                );
                            });
                    }
                    _.L();
                }
                else if (IsRestXmlService(service))
                {
                    _.L($"var errorType = {protocolRuntime}.DeserializeErrorCode(response.Content);");
                    foreach (var errorId in operation.Errors.OrderBy(id => id.ToString(), StringComparer.Ordinal))
                    {
                        var error = model.GetShape(errorId);
                        _.L($"if (string.Equals(errorType, {FormatString(errorId.Name)}, StringComparison.Ordinal))")
                            .B(_ =>
                            {
                                _.L(
                                    $"return ValueTask.FromResult<Exception?>({GetErrorConstructionExpression(model, service, error, errorId, operation.Id.Namespace, options)});"
                                );
                            });
                    }
                    _.L();
                }
                else
                {
                    foreach (var errorId in operation.Errors.OrderBy(id => id.ToString(), StringComparer.Ordinal))
                    {
                        var error = model.GetShape(errorId);
                        if (GetHttpErrorCode(error) is not { } statusCode)
                        {
                            continue;
                        }

                        _.L();
                        _.L($"if ((int)response.StatusCode == {statusCode.ToString(CultureInfo.InvariantCulture)})")
                            .B(_ =>
                            {
                                _.L(
                                    $"return ValueTask.FromResult<Exception?>({GetErrorConstructionExpression(model, service, error, errorId, operation.Id.Namespace, options)});"
                                );
                            });
                    }
                }

                var fallbackErrorId = operation.Errors.OrderBy(id => id.ToString(), StringComparer.Ordinal).First();
                var fallbackError = model.GetShape(fallbackErrorId);
                _.L();
                _.L(
                    $"return ValueTask.FromResult<Exception?>({GetErrorConstructionExpression(model, service, fallbackError, fallbackErrorId, operation.Id.Namespace, options)});"
                );
            });
        _.L();
    }

    private static string GetErrorConstructionExpression(
        SmithyModel model,
        ModelShape service,
        ModelShape error,
        ShapeId errorId,
        string currentNamespace,
        CSharpGenerationOptions options
    )
    {
        var errorType = GetTypeReference(errorId, currentNamespace, options.BaseNamespace);
        var protocolRuntime = GetClientProtocolRuntimeType(service);
        var messageMember = GetErrorMessageMember(error);
        var bodyMembers = GetResponseBodyMembers(model, error, options);
        string? bodyVariable = null;
        string? bodyInitialization = null;
        if (bodyMembers.Length > 0)
        {
            var bodyType = GetBodyProjectionTypeName(error);
            var requiresBody = bodyMembers.Any(member => IsRequiredHttpOutputMember(error, member, options));
            bodyInitialization =
                $"var body = {(requiresBody ? $"{protocolRuntime}.DeserializeRequiredBody<{bodyType}>(DocumentCodec, response.Content)" : $"{protocolRuntime}.DeserializeBody<{bodyType}>(DocumentCodec, response.Content)")};";
            bodyVariable = "body";
        }

        var arguments = new List<string>
        {
            messageMember is null
                ? "null"
                : GetResponseMemberExpression(
                    model,
                    service,
                    error,
                    messageMember,
                    currentNamespace,
                    options,
                    isError: true,
                    bodyVariable: bodyVariable
                ),
        };
        arguments.AddRange(
            GetSortedMembers(error, messageMember)
                .Select(member =>
                    GetResponseMemberExpression(
                        model,
                        service,
                        error,
                        member,
                        currentNamespace,
                        options,
                        isError: true,
                        bodyVariable: bodyVariable
                    )
                )
        );
        var construction = $"new {errorType}({string.Join(", ", arguments)})";
        if (bodyInitialization is null)
        {
            return construction;
        }

        return $"new Func<{errorType}>(() => {{ {bodyInitialization} return {construction}; }}).Invoke()";
    }

    private static int? GetHttpErrorCode(ModelShape error)
    {
        return error.Traits.GetValueOrDefault(SmithyPrelude.HttpErrorTrait) is { } value ? (int)value.AsNumber() : null;
    }

    private static HttpBinding ReadHttpBinding(ModelShape operation)
    {
        if (operation.Traits.GetValueOrDefault(SmithyPrelude.HttpTrait) is not { } value)
        {
            throw new SmithyException(
                $"Operation '{operation.Id}' cannot be generated for an HTTP protocol because it is missing the @http trait."
            );
        }

        var properties = value.AsObject();
        var method = properties.TryGetValue("method", out var methodValue)
            ? methodValue.AsString()
            : throw new SmithyException($"Operation '{operation.Id}' @http trait is missing method.");
        var uri = properties.TryGetValue("uri", out var uriValue)
            ? uriValue.AsString()
            : throw new SmithyException($"Operation '{operation.Id}' @http trait is missing uri.");
        return new HttpBinding(method, uri);
    }

    private static bool EmitsHttpClient(ModelShape service)
    {
        return service.Traits.Has(SmithyPrelude.RestJson1Trait)
            || service.Traits.Has(SmithyPrelude.RestXmlTrait)
            || service.Traits.Has(SmithyPrelude.SimpleRestJsonTrait)
            || service.Traits.Has(SmithyPrelude.RpcV2CborTrait);
    }

    private static bool EmitsGrpcClient(ModelShape service)
    {
        return service.Traits.Has(SmithyPrelude.GrpcTrait);
    }

    private static bool IsRestXmlService(ModelShape service)
    {
        return service.Traits.Has(SmithyPrelude.RestXmlTrait);
    }

    private static bool IsRpcV2CborService(ModelShape service)
    {
        return service.Traits.Has(SmithyPrelude.RpcV2CborTrait);
    }

    private static string GetClientProtocolRuntimeType(ModelShape service)
    {
        if (IsRestXmlService(service))
        {
            return "RestXmlClientProtocol";
        }

        return IsRpcV2CborService(service) ? "RpcV2CborClientProtocol" : "RestJsonClientProtocol";
    }

    private static string GetDocumentCodecExpression(ModelShape service)
    {
        if (IsRestXmlService(service))
        {
            return "SmithyXmlPayloadCodec.Default";
        }

        return IsRpcV2CborService(service) ? "SmithyCborPayloadCodec.Default" : "SmithyJsonPayloadCodec.Default";
    }

    private static MemberShape[] GetRequestBodyMembers(SmithyModel model, ModelShape input, CSharpGenerationOptions options)
    {
        return GetConstructorMembers(model, input, options).Where(IsHttpBodyMember).ToArray();
    }

    private static MemberShape[] GetResponseBodyMembers(SmithyModel model, ModelShape output, CSharpGenerationOptions options)
    {
        return GetConstructorMembers(model, output, options)
            .Where(member =>
                !IsHttpHeaderMember(member)
                && !IsHttpPrefixHeadersMember(member)
                && !IsHttpResponseCodeMember(member)
                && !IsHttpPayloadMember(member)
            )
            .ToArray();
    }

    private static string GetBodyProjectionTypeName(ModelShape shape)
    {
        return $"{GetTypeName(shape.Id)}HttpBody";
    }

    private static void AddHttpBodyProjectionTypes(ITextBuilder _, SmithyModel model, ModelShape service, CSharpGenerationOptions options)
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
                    AddBodyProjectionType(_, model, service, input, inputBodyMembers, options);
                    _.L();
                }
            }

            if (operation.Output is not { } outputId || !emitted.Add(outputId))
            {
                continue;
            }

            var output = model.GetShape(outputId);
            if (!HasResponseBindings(output))
            {
                continue;
            }

            var bodyMembers = GetResponseBodyMembers(model, output, options);
            if (bodyMembers.Length == 0)
            {
                continue;
            }

            AddBodyProjectionType(_, model, service, output, bodyMembers, options);
            _.L();
        }

        foreach (var operationId in service.Operations.OrderBy(id => id.ToString(), StringComparer.Ordinal))
        {
            var operation = model.GetShape(operationId);
            foreach (var errorId in operation.Errors.OrderBy(id => id.ToString(), StringComparer.Ordinal))
            {
                if (!emitted.Add(errorId))
                {
                    continue;
                }

                var error = model.GetShape(errorId);
                var bodyMembers = GetResponseBodyMembers(model, error, options);
                if (bodyMembers.Length == 0)
                {
                    continue;
                }

                AddBodyProjectionType(_, model, service, error, bodyMembers, options);
                _.L();
            }
        }
    }

    private static void AddBodyProjectionType(
        ITextBuilder _,
        SmithyModel model,
        ModelShape service,
        ModelShape shape,
        MemberShape[] bodyMembers,
        CSharpGenerationOptions options
    )
    {
        var typeName = GetBodyProjectionTypeName(shape);
        AddShapeAttributes(_, shape);
        _.L($"private sealed class {typeName}")
            .B(_ =>
            {
                _.L($"public {typeName}(")
                    .B(
                        builder =>
                        {
                            for (var i = 0; i < bodyMembers.Length; i++)
                            {
                                var member = bodyMembers[i];
                                var memberType = GetMemberType(model, shape, member, service.Id.Namespace, options);
                                var suffix = i == bodyMembers.Length - 1 ? string.Empty : ",";
                                builder.L($"{memberType} {CSharpIdentifier.ParameterName(member.Name)}{suffix}");
                            }

                            builder.L(")");
                        },
                        ConfigureTextBlock(BlockStyle.IndentOnly)
                    );
                _.L("{")
                    .B(
                        _ =>
                        {
                            foreach (var member in bodyMembers)
                            {
                                var propertyName = CSharpIdentifier.PropertyName(member.Name);
                                var parameterName = CSharpIdentifier.ParameterName(member.Name);
                                _.L($"{propertyName} = {parameterName};");
                            }
                        },
                        ConfigureTextBlock(BlockStyle.IndentOnly)
                    );
                _.L("}");
                _.L();

                foreach (var member in bodyMembers)
                {
                    var memberType = GetMemberType(model, shape, member, service.Id.Namespace, options);
                    AddMemberAttributes(_, member, isSparse: IsSparseTarget(model, member.Target));
                    _.L($"public {memberType} {CSharpIdentifier.PropertyName(member.Name)} {{ get; }}");
                    _.L();
                }
            });
    }

    private static void AddBodyProjectionType(
        CSharpWriter builder,
        SmithyModel model,
        ModelShape service,
        ModelShape shape,
        MemberShape[] bodyMembers,
        CSharpGenerationOptions options
    )
    {
        var typeName = GetBodyProjectionTypeName(shape);
        builder.Line($"[SmithyShape({FormatString(shape.Id.ToString())}, ShapeKind.Structure)]");
        AddTraitAttributes(builder, shape.Traits);
        builder.Line($"private sealed class {typeName}");
        builder.Block(() =>
        {
            builder.Line($"public {typeName}(");
            builder.Indented(() =>
            {
                for (var i = 0; i < bodyMembers.Length; i++)
                {
                    var member = bodyMembers[i];
                    var memberType = GetMemberType(model, shape, member, service.Id.Namespace, options);
                    var suffix = i == bodyMembers.Length - 1 ? string.Empty : ",";
                    builder.Line($"{memberType} {CSharpIdentifier.ParameterName(member.Name)}{suffix}");
                }
            });
            builder.Line(")");
            builder.Block(() =>
            {
                foreach (var member in bodyMembers)
                {
                    var propertyName = CSharpIdentifier.PropertyName(member.Name);
                    var parameterName = CSharpIdentifier.ParameterName(member.Name);
                    builder.Line($"{propertyName} = {parameterName};");
                }
            });
            builder.Line();

            foreach (var member in bodyMembers)
            {
                var memberType = GetMemberType(model, shape, member, service.Id.Namespace, options);
                AddMemberAttributes(builder, member, isSparse: IsSparseTarget(model, member.Target));
                builder.Line($"public {memberType} {CSharpIdentifier.PropertyName(member.Name)} {{ get; }}");
                builder.Line();
            }
        });
    }

    private static void AddMemberAttributes(CSharpWriter builder, MemberShape member, bool isSparse)
    {
        var arguments = new List<string> { FormatString(member.Name), FormatString(member.Target.ToString()) };
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
        AddTraitAttributes(builder, member.Traits);
    }

    private static void AddTraitAttributes(CSharpWriter builder, TraitCollection traits)
    {
        foreach (var trait in traits.OrderBy(trait => trait.Key.ToString(), StringComparer.Ordinal))
        {
            var value = GetTraitAttributeValue(trait.Value);
            var valueInitializer = value is null ? string.Empty : $", Value = {FormatString(value)}";
            builder.Line($"[SmithyTrait({FormatString(trait.Key.ToString())}{valueInitializer})]");
        }
    }

    private static string GetDocumentMemberName(MemberShape member, ModelShape service)
    {
        if (IsRestXmlService(service))
        {
            return member.Traits.GetValueOrDefault(SmithyPrelude.XmlNameTrait)?.AsString() ?? member.Name;
        }

        return member.Traits.GetValueOrDefault(SmithyPrelude.JsonNameTrait)?.AsString() ?? member.Name;
    }
}

#pragma warning restore CA1305
