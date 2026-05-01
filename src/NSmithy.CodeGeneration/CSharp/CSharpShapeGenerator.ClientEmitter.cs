using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using NSmithy.CodeGeneration.Model;
using NSmithy.Core;

namespace NSmithy.CodeGeneration.CSharp;

#pragma warning disable CA1305 // Source text is assembled from Smithy identifiers and explicitly formatted literals.

public sealed partial class CSharpShapeGenerator
{
    private static GeneratedCSharpFile GenerateClient(
        SmithyModel model,
        ModelShape service,
        CSharpGenerationOptions options
    )
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

        var builder = CreateFileBuilder(
            service,
            options,
            extraUsings.Distinct(StringComparer.Ordinal).ToArray()
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

        if (emitsHttp)
        {
            builder.Line($"public sealed class {typeName} : {interfaceName}");
            builder.Block(() =>
            {
                builder.Line(
                    $"private static readonly ISmithyPayloadCodec DocumentCodec = {GetDocumentCodecExpression(service)};"
                );
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
                builder.Line(
                    $"public {typeName}(HttpClient httpClient, SmithyClientOptions options)"
                );
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
                    AppendHttpOperationMethod(builder, model, service, operationId, options);
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

                AppendHttpBodyProjectionTypes(builder, model, service, options);
            });
            builder.Line();
        }

        if (emitsGrpc)
        {
            AppendGrpcClient(builder, model, service, interfaceName, options);
        }

        return new GeneratedCSharpFile(GetClientPath(service), builder.ToString());
    }

    private static void AppendHttpOperationMethod(
        CSharpWriter builder,
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

            if (isRpcV2Cbor)
            {
                builder.Line($"var requestUri = {FormatString(httpBinding.Uri)};");
            }
            else
            {
                AppendRequestUriBuilder(builder, model, service, inputShape, httpBinding);
            }

            builder.Line(
                $"var request = new SmithyHttpRequest(new HttpMethod({FormatString(httpBinding.Method)}), requestUri);"
            );
            if (isRpcV2Cbor)
            {
                builder.Line("request.Headers[\"Smithy-Protocol\"] = [\"rpc-v2-cbor\"];");
                builder.Line("request.Headers[\"Accept\"] = [DocumentCodec.MediaType];");
            }
            else if (inputShape is not null)
            {
                AppendRequestHeaders(builder, service, inputShape);
            }

            if (isRpcV2Cbor)
            {
                if (operation.Input is not null)
                {
                    builder.Line("request.Content = DocumentCodec.Serialize(input);");
                    builder.Line("request.ContentType = DocumentCodec.MediaType;");
                }
            }
            else if (
                inputShape is not null
                && TryGetPayloadMember(inputShape, out var payloadMember)
            )
            {
                var propertyName = CSharpIdentifier.PropertyName(payloadMember.Name);
                if (
                    GetEffectiveDefaultValue(inputShape, payloadMember, options) is { } defaultValue
                )
                {
                    var memberType = GetMemberType(
                        model,
                        inputShape,
                        payloadMember,
                        service.Id.Namespace,
                        options
                    );
                    var defaultExpression = GetDefaultExpression(
                        model,
                        payloadMember.Target,
                        defaultValue,
                        service.Id.Namespace,
                        options.BaseNamespace
                    );
                    builder.Line(
                        $"if (!EqualityComparer<{memberType}>.Default.Equals(input.{propertyName}, {defaultExpression}))"
                    );
                    builder.Block(() =>
                    {
                        builder.Line(
                            $"request.Content = DocumentCodec.Serialize(input.{propertyName});"
                        );
                        builder.Line("request.ContentType = DocumentCodec.MediaType;");
                    });
                }
                else
                {
                    builder.Line(
                        $"request.Content = DocumentCodec.Serialize(input.{propertyName});"
                    );
                    builder.Line("request.ContentType = DocumentCodec.MediaType;");
                }
            }
            else if (inputShape is not null && HasHttpBody(inputShape))
            {
                AppendRequestBody(builder, inputShape, service);
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
            if (isRpcV2Cbor)
            {
                builder.Line($"{protocolRuntime}.EnsureResponse(response);");
                builder.Line("if (response.StatusCode != HttpStatusCode.OK)");
                builder.Block(() =>
                {
                    builder.Line(
                        "throw new SmithyClientException(response.StatusCode, response.ReasonPhrase);"
                    );
                });
                builder.Line();
            }
            AppendResponseReturn(
                builder,
                model,
                service,
                outputShape,
                outputType,
                service.Id.Namespace,
                options,
                isRpcV2Cbor || isRestXml
            );
        });
        builder.Line();
    }

    private static void AppendGrpcClient(
        CSharpWriter builder,
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

        builder.Line($"public sealed class {grpcClientTypeName} : {interfaceName}");
        builder.Block(() =>
        {
            builder.Line($"private readonly {rawClientType} client;");
            builder.Line();
            builder.Line($"public {grpcClientTypeName}(ChannelBase channel)");
            builder.Indented(() =>
            {
                builder.Line(
                    $": this(new {rawClientType}(channel ?? throw new ArgumentNullException(nameof(channel))))"
                );
            });
            builder.Block(() => { });
            builder.Line();
            builder.Line($"public {grpcClientTypeName}(CallInvoker callInvoker)");
            builder.Indented(() =>
            {
                builder.Line(
                    $": this(new {rawClientType}(callInvoker ?? throw new ArgumentNullException(nameof(callInvoker))))"
                );
            });
            builder.Block(() => { });
            builder.Line();
            builder.Line($"public {grpcClientTypeName}({rawClientType} client)");
            builder.Block(() =>
            {
                builder.Line(
                    "this.client = client ?? throw new ArgumentNullException(nameof(client));"
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
                AppendGrpcOperationMethod(
                    builder,
                    model,
                    service,
                    model.GetShape(operationId),
                    options
                );
                builder.Line();
            }
        });
    }

    private static void AppendGrpcOperationMethod(
        CSharpWriter builder,
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

        builder.Line($"public async {GetOperationSignature(model, service, operation, options)}");
        builder.Block(() =>
        {
            if (operation.Input is not null)
            {
                builder.Line("ArgumentNullException.ThrowIfNull(input);");
                builder.Line();
            }

            builder.Line($"{grpcInputType} request = {grpcInputExpression};");
            if (operation.Output is { } outputId)
            {
                builder.Line(
                    $"var response = await client.{operationName}Async(request, cancellationToken: cancellationToken).ResponseAsync.ConfigureAwait(false);"
                );
                builder.Line(
                    $"return {GetGrpcToSmithyValueExpression(model, outputId, "response", service.Id.Namespace, options)};"
                );
            }
            else
            {
                builder.Line(
                    $"await client.{operationName}Async(request, cancellationToken: cancellationToken).ResponseAsync.ConfigureAwait(false);"
                );
            }
        });
    }

    private static void AppendResponseReturn(
        CSharpWriter builder,
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
            builder.Line(
                $"return {protocolRuntime}.DeserializeRequiredBody<{outputType}>(DocumentCodec, response.Content);"
            );
            return;
        }

        var bodyMembers = GetResponseBodyMembers(model, output, options);
        string? bodyVariable = null;
        if (bodyMembers.Length > 0)
        {
            var bodyType = GetBodyProjectionTypeName(output);
            var requiresBody = bodyMembers.Any(member =>
                IsRequiredHttpOutputMember(output, member, options)
            );
            builder.Line(
                $"var body = {(requiresBody ? $"{protocolRuntime}.DeserializeRequiredBody<{bodyType}>(DocumentCodec, response.Content)" : $"{protocolRuntime}.DeserializeBody<{bodyType}>(DocumentCodec, response.Content)")};"
            );
            builder.Line();
            bodyVariable = "body";
        }

        builder.Line($"return new {outputType}(");
        builder.Indented(() =>
        {
            var members = GetConstructorMembers(model, output, options);
            for (var i = 0; i < members.Length; i++)
            {
                var suffix = i == members.Length - 1 ? string.Empty : ",";
                builder.Line(
                    $"{GetResponseMemberExpression(model, service, output, members[i], currentNamespace, options, bodyVariable: bodyVariable)}{suffix}"
                );
            }
        });
        builder.Line(");");
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

    private static bool IsRequiredHttpOutputMember(
        ModelShape container,
        MemberShape member,
        CSharpGenerationOptions options
    )
    {
        return member.IsRequired && GetEffectiveDefaultValue(container, member, options) is null;
    }

    private static void AppendRequestUriBuilder(
        CSharpWriter builder,
        SmithyModel model,
        ModelShape service,
        ModelShape? input,
        HttpBinding httpBinding
    )
    {
        var protocolRuntime = GetClientProtocolRuntimeType(service);
        builder.Line(
            $"var requestUriBuilder = new StringBuilder({FormatString(httpBinding.Uri)});"
        );
        if (input is not null)
        {
            foreach (var member in GetSortedMembers(input).Where(IsHttpLabelMember))
            {
                var propertyName = CSharpIdentifier.PropertyName(member.Name);
                var labelVariableName = $"{CSharpIdentifier.ParameterName(member.Name)}Label";
                var labelExpression = IsReferenceType(model, member.Target)
                    ? $"input.{propertyName} ?? throw new ArgumentException({FormatString($"HTTP label '{member.Name}' is required.")}, nameof(input))"
                    : $"input.{propertyName}";
                builder.Line($"var {labelVariableName} = {labelExpression};");
                builder.Line(
                    $"requestUriBuilder.Replace({FormatString($"{{{member.Name}+}}")}, {protocolRuntime}.EscapeGreedyLabel({labelVariableName}));"
                );
                builder.Line(
                    $"requestUriBuilder.Replace({FormatString($"{{{member.Name}}}")}, Uri.EscapeDataString({protocolRuntime}.FormatHttpValue({labelVariableName})));"
                );
            }

            foreach (var member in GetSortedMembers(input).Where(IsHttpQueryMember))
            {
                var queryName = member.Traits[SmithyPrelude.HttpQueryTrait].AsString();
                var propertyName = CSharpIdentifier.PropertyName(member.Name);
                builder.Line(
                    $"{protocolRuntime}.AppendQuery(requestUriBuilder, {FormatString(queryName)}, input.{propertyName});"
                );
            }

            foreach (var member in GetSortedMembers(input).Where(IsHttpQueryParamsMember))
            {
                var propertyName = CSharpIdentifier.PropertyName(member.Name);
                builder.Line(
                    $"{protocolRuntime}.AppendQueryMap(requestUriBuilder, input.{propertyName});"
                );
            }
        }

        builder.Line("var requestUri = requestUriBuilder.ToString();");
    }

    private static void AppendRequestHeaders(
        CSharpWriter builder,
        ModelShape service,
        ModelShape input
    )
    {
        var protocolRuntime = GetClientProtocolRuntimeType(service);
        foreach (var member in GetSortedMembers(input).Where(IsHttpHeaderMember))
        {
            var headerName = member.Traits[SmithyPrelude.HttpHeaderTrait].AsString();
            var propertyName = CSharpIdentifier.PropertyName(member.Name);
            builder.Line(
                $"{protocolRuntime}.AddHeader(request.Headers, {FormatString(headerName)}, input.{propertyName});"
            );
        }

        foreach (var member in GetSortedMembers(input).Where(IsHttpPrefixHeadersMember))
        {
            var headerPrefix = member.Traits[SmithyPrelude.HttpPrefixHeadersTrait].AsString();
            var propertyName = CSharpIdentifier.PropertyName(member.Name);
            builder.Line(
                $"{protocolRuntime}.AddPrefixedHeaders(request.Headers, {FormatString(headerPrefix)}, input.{propertyName});"
            );
        }
    }

    private static void AppendRequestBody(
        CSharpWriter builder,
        ModelShape input,
        ModelShape service
    )
    {
        var bodyMembers = GetSortedMembers(input).Where(IsHttpBodyMember).ToArray();
        if (bodyMembers.Length == 0)
        {
            return;
        }

        var bodyType = GetBodyProjectionTypeName(input);
        builder.Line($"var requestBody = new {bodyType}(");
        builder.Indented(() =>
        {
            for (var i = 0; i < bodyMembers.Length; i++)
            {
                var member = bodyMembers[i];
                var propertyName = CSharpIdentifier.PropertyName(member.Name);
                var suffix = i == bodyMembers.Length - 1 ? string.Empty : ",";
                builder.Line($"input.{propertyName}{suffix}");
            }
        });
        builder.Line(");");
        builder.Line("request.Content = DocumentCodec.Serialize(requestBody);");
        builder.Line("request.ContentType = DocumentCodec.MediaType;");
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

    private static bool IsHttpHeaderMember(MemberShape member) =>
        member.Traits.Has(SmithyPrelude.HttpHeaderTrait);

    private static bool IsHttpLabelMember(MemberShape member) =>
        member.Traits.Has(SmithyPrelude.HttpLabelTrait);

    private static bool IsHttpPayloadMember(MemberShape member) =>
        member.Traits.Has(SmithyPrelude.HttpPayloadTrait);

    private static bool IsHttpPrefixHeadersMember(MemberShape member) =>
        member.Traits.Has(SmithyPrelude.HttpPrefixHeadersTrait);

    private static bool IsHttpQueryMember(MemberShape member) =>
        member.Traits.Has(SmithyPrelude.HttpQueryTrait);

    private static bool IsHttpQueryParamsMember(MemberShape member) =>
        member.Traits.Has(SmithyPrelude.HttpQueryParamsTrait);

    private static bool IsHttpResponseCodeMember(MemberShape member) =>
        member.Traits.Has(SmithyPrelude.HttpResponseCodeTrait);

    private static bool HasResponseBindings(ModelShape output)
    {
        return output.Members.Values.Any(member =>
            IsHttpHeaderMember(member)
            || IsHttpPrefixHeadersMember(member)
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
        var service = model.Shapes.Values.First(shape =>
            shape.Kind == ShapeKind.Service && shape.Operations.Contains(operationId)
        );
        var protocolRuntime = GetClientProtocolRuntimeType(service);
        var isRpcV2Cbor = IsRpcV2CborService(service);
        var methodName = $"Deserialize{CSharpIdentifier.TypeName(operation.Id.Name)}ErrorAsync";
        builder.Line(
            $"private static ValueTask<Exception?> {methodName}(SmithyHttpResponse response, CancellationToken cancellationToken)"
        );
        builder.Block(() =>
        {
            builder.Line("if (response.Content.Length == 0)");
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

            if (isRpcV2Cbor)
            {
                builder.Line($"if (!{protocolRuntime}.HasResponse(response))");
                builder.Block(() =>
                {
                    builder.Line("return ValueTask.FromResult<Exception?>(null);");
                });
                builder.Line();
                builder.Line(
                    $"var errorType = {protocolRuntime}.DeserializeErrorType(response.Content);"
                );
                foreach (
                    var errorId in operation.Errors.OrderBy(
                        id => id.ToString(),
                        StringComparer.Ordinal
                    )
                )
                {
                    var error = model.GetShape(errorId);
                    builder.Line(
                        $"if (string.Equals(errorType, {FormatString(errorId.Name)}, StringComparison.Ordinal) || string.Equals(errorType, {FormatString(errorId.ToString())}, StringComparison.Ordinal))"
                    );
                    builder.Block(() =>
                    {
                        builder.Line(
                            $"return ValueTask.FromResult<Exception?>({GetErrorConstructionExpression(model, service, error, errorId, operation.Id.Namespace, options)});"
                        );
                    });
                }
                builder.Line();
            }
            else if (IsRestXmlService(service))
            {
                builder.Line(
                    $"var errorType = {protocolRuntime}.DeserializeErrorCode(response.Content);"
                );
                foreach (
                    var errorId in operation.Errors.OrderBy(
                        id => id.ToString(),
                        StringComparer.Ordinal
                    )
                )
                {
                    var error = model.GetShape(errorId);
                    builder.Line(
                        $"if (string.Equals(errorType, {FormatString(errorId.Name)}, StringComparison.Ordinal))"
                    );
                    builder.Block(() =>
                    {
                        builder.Line(
                            $"return ValueTask.FromResult<Exception?>({GetErrorConstructionExpression(model, service, error, errorId, operation.Id.Namespace, options)});"
                        );
                    });
                }
                builder.Line();
            }
            else
            {
                foreach (
                    var errorId in operation.Errors.OrderBy(
                        id => id.ToString(),
                        StringComparer.Ordinal
                    )
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
                            $"return ValueTask.FromResult<Exception?>({GetErrorConstructionExpression(model, service, error, errorId, operation.Id.Namespace, options)});"
                        );
                    });
                }
            }

            var fallbackErrorId = operation
                .Errors.OrderBy(id => id.ToString(), StringComparer.Ordinal)
                .First();
            var fallbackError = model.GetShape(fallbackErrorId);
            builder.Line();
            builder.Line(
                $"return ValueTask.FromResult<Exception?>({GetErrorConstructionExpression(model, service, fallbackError, fallbackErrorId, operation.Id.Namespace, options)});"
            );
        });
        builder.Line();
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
            var requiresBody = bodyMembers.Any(member =>
                IsRequiredHttpOutputMember(error, member, options)
            );
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
        return error.Traits.GetValueOrDefault(SmithyPrelude.HttpErrorTrait) is { } value
            ? (int)value.AsNumber()
            : null;
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
            : throw new SmithyException(
                $"Operation '{operation.Id}' @http trait is missing method."
            );
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

        return IsRpcV2CborService(service)
            ? "SmithyCborPayloadCodec.Default"
            : "SmithyJsonPayloadCodec.Default";
    }

    private static MemberShape[] GetRequestBodyMembers(
        SmithyModel model,
        ModelShape input,
        CSharpGenerationOptions options
    )
    {
        return GetConstructorMembers(model, input, options).Where(IsHttpBodyMember).ToArray();
    }

    private static MemberShape[] GetResponseBodyMembers(
        SmithyModel model,
        ModelShape output,
        CSharpGenerationOptions options
    )
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

    private static void AppendHttpBodyProjectionTypes(
        CSharpWriter builder,
        SmithyModel model,
        ModelShape service,
        CSharpGenerationOptions options
    )
    {
        var emitted = new HashSet<ShapeId>();
        foreach (
            var operationId in service.Operations.OrderBy(
                id => id.ToString(),
                StringComparer.Ordinal
            )
        )
        {
            var operation = model.GetShape(operationId);
            if (operation.Input is { } inputId && emitted.Add(inputId))
            {
                var input = model.GetShape(inputId);
                var inputBodyMembers = GetRequestBodyMembers(model, input, options);
                if (inputBodyMembers.Length > 0)
                {
                    AppendBodyProjectionType(
                        builder,
                        model,
                        service,
                        input,
                        inputBodyMembers,
                        options
                    );
                    builder.Line();
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

            AppendBodyProjectionType(builder, model, service, output, bodyMembers, options);
            builder.Line();
        }

        foreach (
            var operationId in service.Operations.OrderBy(
                id => id.ToString(),
                StringComparer.Ordinal
            )
        )
        {
            var operation = model.GetShape(operationId);
            foreach (
                var errorId in operation.Errors.OrderBy(id => id.ToString(), StringComparer.Ordinal)
            )
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

                AppendBodyProjectionType(builder, model, service, error, bodyMembers, options);
                builder.Line();
            }
        }
    }

    private static void AppendBodyProjectionType(
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
        AppendTraitAttributes(builder, shape.Traits);
        builder.Line($"private sealed class {typeName}");
        builder.Block(() =>
        {
            builder.Line($"public {typeName}(");
            builder.Indented(() =>
            {
                for (var i = 0; i < bodyMembers.Length; i++)
                {
                    var member = bodyMembers[i];
                    var memberType = GetMemberType(
                        model,
                        shape,
                        member,
                        service.Id.Namespace,
                        options
                    );
                    var suffix = i == bodyMembers.Length - 1 ? string.Empty : ",";
                    builder.Line(
                        $"{memberType} {CSharpIdentifier.ParameterName(member.Name)}{suffix}"
                    );
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
                AppendMemberAttributes(
                    builder,
                    member,
                    isSparse: IsSparseTarget(model, member.Target)
                );
                builder.Line(
                    $"public {memberType} {CSharpIdentifier.PropertyName(member.Name)} {{ get; }}"
                );
                builder.Line();
            }
        });
    }

    private static string GetDocumentMemberName(MemberShape member, ModelShape service)
    {
        if (IsRestXmlService(service))
        {
            return member.Traits.GetValueOrDefault(SmithyPrelude.XmlNameTrait)?.AsString()
                ?? member.Name;
        }

        return member.Traits.GetValueOrDefault(SmithyPrelude.JsonNameTrait)?.AsString()
            ?? member.Name;
    }
}

#pragma warning restore CA1305
