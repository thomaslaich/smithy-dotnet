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
        var emitsHttp = EmitsHttpClient(service);
        var emitsGrpc = service.Traits.Has(SmithyPrelude.GrpcTrait);
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
                "SmithyNet.Client",
                "SmithyNet.Http",
                "SmithyNet.Codecs",
            ]);

            if (IsRestXmlService(service))
            {
                extraUsings.Add("SmithyNet.Codecs.Xml");
            }
            else if (IsRpcV2CborService(service))
            {
                extraUsings.Add("SmithyNet.Codecs.Cbor");
            }
            else
            {
                extraUsings.Add("SmithyNet.Codecs.Json");
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

                AppendClientHelpers(builder, service);
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
                AppendRequestUriBuilder(builder, model, inputShape, httpBinding);
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
                AppendRequestHeaders(builder, inputShape);
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
                builder.Line("EnsureRpcV2CborResponse(response);");
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
        if (useDocumentBindings || !HasResponseBindings(output))
        {
            builder.Line($"return DeserializeRequiredBody<{outputType}>(response.Content);");
            return;
        }

        builder.Line($"return new {outputType}(");
        builder.Indented(() =>
        {
            var members = GetConstructorMembers(model, output, options);
            for (var i = 0; i < members.Length; i++)
            {
                var suffix = i == members.Length - 1 ? string.Empty : ",";
                builder.Line(
                    $"{GetResponseMemberExpression(model, service, output, members[i], currentNamespace, options)}{suffix}"
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
        bool isError = false
    )
    {
        var memberType = GetMemberParameterType(model, output, member, currentNamespace, options);
        var required = IsRequiredHttpOutputMember(output, member, options);
        if (IsHttpHeaderMember(member))
        {
            var headerName = member.Traits[SmithyPrelude.HttpHeaderTrait].AsString();
            return required
                ? $"GetRequiredHeader<{memberType}>(response.Headers, {FormatString(headerName)})"
                : $"GetHeader<{memberType}>(response.Headers, {FormatString(headerName)})";
        }

        if (IsHttpPrefixHeadersMember(member))
        {
            var headerPrefix = member.Traits[SmithyPrelude.HttpPrefixHeadersTrait].AsString();
            return required
                ? $"GetRequiredPrefixedHeaders<{memberType}>(response.Headers, {FormatString(headerPrefix)})"
                : $"GetPrefixedHeaders<{memberType}>(response.Headers, {FormatString(headerPrefix)})";
        }

        if (IsHttpResponseCodeMember(member))
        {
            return $"({memberType})(int)response.StatusCode";
        }

        if (IsHttpPayloadMember(member))
        {
            return required
                ? $"DeserializeRequiredBody<{memberType}>(response.Content)"
                : $"DeserializeBody<{memberType}>(response.Content)";
        }

        var memberName = GetDocumentMemberName(member, service);
        if (isError && IsRestXmlService(service))
        {
            return required
                ? $"DeserializeRequiredRestXmlErrorBodyMember<{memberType}>(response.Content, {FormatString(memberName)})"
                : $"DeserializeRestXmlErrorBodyMember<{memberType}>(response.Content, {FormatString(memberName)})";
        }

        return required
            ? $"DeserializeRequiredBodyMember<{memberType}>(response.Content, {FormatString(memberName)})"
            : $"DeserializeBodyMember<{memberType}>(response.Content, {FormatString(memberName)})";
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
                var labelExpression = IsReferenceType(model, member.Target)
                    ? $"input.{propertyName} ?? throw new ArgumentException({FormatString($"HTTP label '{member.Name}' is required.")}, nameof(input))"
                    : $"input.{propertyName}";
                builder.Line($"var {labelVariableName} = {labelExpression};");
                builder.Line(
                    $"requestUriBuilder.Replace({FormatString($"{{{member.Name}+}}")}, EscapeGreedyLabel({labelVariableName}));"
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

            foreach (var member in GetSortedMembers(input).Where(IsHttpQueryParamsMember))
            {
                var propertyName = CSharpIdentifier.PropertyName(member.Name);
                builder.Line($"AppendQueryMap(requestUriBuilder, input.{propertyName});");
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

        foreach (var member in GetSortedMembers(input).Where(IsHttpPrefixHeadersMember))
        {
            var headerPrefix = member.Traits[SmithyPrelude.HttpPrefixHeadersTrait].AsString();
            var propertyName = CSharpIdentifier.PropertyName(member.Name);
            builder.Line(
                $"AddPrefixedHeaders(request.Headers, {FormatString(headerPrefix)}, input.{propertyName});"
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

        builder.Line("var requestBody = new Dictionary<string, object?>");
        builder.Block(
            () =>
            {
                foreach (var member in bodyMembers)
                {
                    var propertyName = CSharpIdentifier.PropertyName(member.Name);
                    builder.Line(
                        $"[{FormatString(GetDocumentMemberName(member, service))}] = input.{propertyName},"
                    );
                }
            },
            closingSuffix: ";"
        );
        if (IsRestXmlService(service))
        {
            builder.Line(
                $"request.Content = Encoding.UTF8.GetBytes(SmithyXmlSerializer.SerializeMembers({FormatString(GetDocumentRootName(input, service))}, requestBody));"
            );
        }
        else
        {
            builder.Line("request.Content = DocumentCodec.Serialize(requestBody);");
        }
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
        var isRpcV2Cbor = IsRpcV2CborService(service);
        var methodName = $"Deserialize{CSharpIdentifier.TypeName(operation.Id.Name)}ErrorAsync";
        builder.Line(
            $"private static ValueTask<Exception?> {methodName}(SmithyHttpResponse response, CancellationToken cancellationToken)"
        );
        builder.Block(() =>
        {
            builder.Line("if (IsEmptyContent(response.Content))");
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
                builder.Line("if (!HasRpcV2CborResponse(response))");
                builder.Block(() =>
                {
                    builder.Line("return ValueTask.FromResult<Exception?>(null);");
                });
                builder.Line();
                builder.Line(
                    "var errorType = DeserializeBodyMember<string?>(response.Content, \"__type\");"
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
                builder.Line("var errorType = DeserializeRestXmlErrorCode(response.Content);");
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
        var messageMember = GetErrorMessageMember(error);
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
                    isError: true
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
                        isError: true
                    )
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

    private static bool IsRestXmlService(ModelShape service)
    {
        return service.Traits.Has(SmithyPrelude.RestXmlTrait);
    }

    private static bool IsRpcV2CborService(ModelShape service)
    {
        return service.Traits.Has(SmithyPrelude.RpcV2CborTrait);
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

    private static string GetDocumentRootName(ModelShape shape, ModelShape service)
    {
        return IsRestXmlService(service)
            ? shape.Traits.GetValueOrDefault(SmithyPrelude.XmlNameTrait)?.AsString()
                ?? shape.Id.Name
            : shape.Id.Name;
    }

    private static void AppendClientHelpers(CSharpWriter builder, ModelShape service)
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
            "private static void AddPrefixedHeaders(IDictionary<string, IReadOnlyList<string>> headers, string prefix, object? value)"
        );
        builder.Block(() =>
        {
            builder.Line("if (value is null)");
            builder.Block(() =>
            {
                builder.Line("return;");
            });
            builder.Line();
            builder.Line("foreach (var item in EnumerateStringMap(value))");
            builder.Block(() =>
            {
                builder.Line("if (item.Value is null)");
                builder.Block(() =>
                {
                    builder.Line("continue;");
                });
                builder.Line();
                builder.Line("headers[$\"{prefix}{item.Key}\"] = [FormatHttpValue(item.Value)];");
            });
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

        builder.Line("private static void AppendQueryMap(StringBuilder builder, object? value)");
        builder.Block(() =>
        {
            builder.Line("if (value is null)");
            builder.Block(() =>
            {
                builder.Line("return;");
            });
            builder.Line();
            builder.Line("foreach (var item in EnumerateStringMap(value))");
            builder.Block(() =>
            {
                builder.Line("AppendQueryValue(builder, item.Key, item.Value);");
            });
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

        builder.Line(
            "private static IEnumerable<KeyValuePair<string, object?>> EnumerateStringMap(object value)"
        );
        builder.Block(() =>
        {
            builder.Line(
                "var values = value is IDictionary ? value : value.GetType().GetProperty(\"Values\")?.GetValue(value);"
            );
            builder.Line("if (values is not IEnumerable enumerable)");
            builder.Block(() =>
            {
                builder.Line("yield break;");
            });
            builder.Line();
            builder.Line("foreach (var item in enumerable)");
            builder.Block(() =>
            {
                builder.Line("if (item is null)");
                builder.Block(() =>
                {
                    builder.Line("continue;");
                });
                builder.Line();
                builder.Line("if (item is DictionaryEntry dictionaryEntry)");
                builder.Block(() =>
                {
                    builder.Line("if (dictionaryEntry.Key is not null)");
                    builder.Block(() =>
                    {
                        builder.Line(
                            "yield return new KeyValuePair<string, object?>(dictionaryEntry.Key.ToString() ?? string.Empty, dictionaryEntry.Value);"
                        );
                    });
                    builder.Line();
                    builder.Line("continue;");
                });
                builder.Line();
                builder.Line("var itemType = item.GetType();");
                builder.Line(
                    "var key = itemType.GetProperty(\"Key\")?.GetValue(item)?.ToString();"
                );
                builder.Line("if (key is null)");
                builder.Block(() =>
                {
                    builder.Line("continue;");
                });
                builder.Line();
                builder.Line(
                    "yield return new KeyValuePair<string, object?>(key, itemType.GetProperty(\"Value\")?.GetValue(item));"
                );
            });
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
                        "Enum enumValue => Convert.ToInt32(enumValue, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),"
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

        builder.Line("private static string EscapeGreedyLabel(object value)");
        builder.Block(() =>
        {
            builder.Line(
                "return string.Join(\"/\", FormatHttpValue(value).Split('/').Select(Uri.EscapeDataString));"
            );
        });
        builder.Line();

        builder.Line("private static bool IsEmptyContent(byte[] content)");
        builder.Block(() =>
        {
            builder.Line("return content.Length == 0;");
        });
        builder.Line();

        builder.Line("private static T DeserializeBody<T>(byte[] content)");
        builder.Block(() =>
        {
            builder.Line("return IsEmptyContent(content)");
            builder.Indented(() =>
            {
                builder.Line("? default!");
                builder.Line(": DocumentCodec.Deserialize<T>(content);");
            });
        });
        builder.Line();

        builder.Line("private static T DeserializeRequiredBody<T>(byte[] content)");
        builder.Block(() =>
        {
            builder.Line("if (IsEmptyContent(content))");
            builder.Block(() =>
            {
                builder.Line(
                    "throw new InvalidOperationException(\"Response body is required but was empty.\");"
                );
            });
            builder.Line();
            builder.Line("return DocumentCodec.Deserialize<T>(content);");
        });
        builder.Line();

        builder.Line("private static T DeserializeBodyMember<T>(byte[] content, string name)");
        builder.Block(() =>
        {
            builder.Line("if (IsEmptyContent(content))");
            builder.Block(() =>
            {
                builder.Line("return default!;");
            });
            builder.Line();
            builder.Line(GetDeserializeBodyMemberExpression(service, required: false));
        });
        builder.Line();

        builder.Line(
            "private static T DeserializeRequiredBodyMember<T>(byte[] content, string name)"
        );
        builder.Block(() =>
        {
            builder.Line("if (IsEmptyContent(content))");
            builder.Block(() =>
            {
                builder.Line(
                    "throw new InvalidOperationException($\"Response body member '{name}' is required but the response body was empty.\");"
                );
            });
            builder.Line();
            builder.Line($"var value = {GetDeserializeBodyMemberCall(service)};");
            builder.Line("return EqualityComparer<T>.Default.Equals(value, default!)");
            builder.Indented(() =>
            {
                builder.Line(
                    "? throw new InvalidOperationException($\"Response body member '{name}' is required but was missing.\")"
                );
                builder.Line(": value;");
            });
        });
        builder.Line();

        builder.Line("private static bool HasRpcV2CborResponse(SmithyHttpResponse response)");
        builder.Block(() =>
        {
            builder.Line(
                "return response.Headers.TryGetValue(\"Smithy-Protocol\", out var values) && values.Any(value => string.Equals(value, \"rpc-v2-cbor\", StringComparison.Ordinal));"
            );
        });
        builder.Line();

        builder.Line("private static void EnsureRpcV2CborResponse(SmithyHttpResponse response)");
        builder.Block(() =>
        {
            builder.Line("if (!HasRpcV2CborResponse(response))");
            builder.Block(() =>
            {
                builder.Line(
                    "throw new InvalidOperationException(\"rpcv2Cbor response is missing the required Smithy-Protocol header.\");"
                );
            });
        });
        builder.Line();

        builder.Line("private static string? DeserializeRestXmlErrorCode(byte[] content)");
        builder.Block(() =>
        {
            builder.Line("var root = GetRestXmlErrorRoot(content);");
            builder.Line(
                "return root.Elements().FirstOrDefault(element => element.Name.LocalName == \"Code\")?.Value;"
            );
        });
        builder.Line();

        builder.Line(
            "private static T DeserializeRestXmlErrorBodyMember<T>(byte[] content, string name)"
        );
        builder.Block(() =>
        {
            builder.Line("var root = GetRestXmlErrorRoot(content);");
            builder.Line(
                "var element = root.Elements().FirstOrDefault(child => child.Name.LocalName == name);"
            );
            builder.Line("return element is null");
            builder.Indented(() =>
            {
                builder.Line("? default!");
                builder.Line(": DeserializeXmlElement<T>(element);");
            });
        });
        builder.Line();

        builder.Line(
            "private static T DeserializeRequiredRestXmlErrorBodyMember<T>(byte[] content, string name)"
        );
        builder.Block(() =>
        {
            builder.Line("var root = GetRestXmlErrorRoot(content);");
            builder.Line(
                "var element = root.Elements().FirstOrDefault(child => child.Name.LocalName == name);"
            );
            builder.Line("if (element is null)");
            builder.Block(() =>
            {
                builder.Line(
                    "throw new InvalidOperationException($\"Response body member '{name}' is required but was missing.\");"
                );
            });
            builder.Line();
            builder.Line("return DeserializeXmlElement<T>(element);");
        });
        builder.Line();

        builder.Line("private static XElement GetRestXmlErrorRoot(byte[] content)");
        builder.Block(() =>
        {
            builder.Line("var document = XDocument.Parse(Encoding.UTF8.GetString(content));");
            builder.Line(
                "var root = document.Root ?? throw new InvalidOperationException(\"Response body was missing an XML root element.\");"
            );
            builder.Line(
                "return string.Equals(root.Name.LocalName, \"ErrorResponse\", StringComparison.Ordinal)"
            );
            builder.Indented(() =>
            {
                builder.Line(
                    "? root.Elements().FirstOrDefault(element => element.Name.LocalName == \"Error\") ?? root"
                );
                builder.Line(": root;");
            });
        });
        builder.Line();

        builder.Line("private static T DeserializeXmlElement<T>(XElement element)");
        builder.Block(() =>
        {
            builder.Line(
                "return DocumentCodec.Deserialize<T>(Encoding.UTF8.GetBytes(element.ToString(SaveOptions.DisableFormatting)));"
            );
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

        builder.Line(
            "private static T GetRequiredHeader<T>(IReadOnlyDictionary<string, IReadOnlyList<string>> headers, string name)"
        );
        builder.Block(() =>
        {
            builder.Line("return headers.TryGetValue(name, out var values) && values.Count > 0");
            builder.Indented(() =>
            {
                builder.Line("? ConvertHttpValue<T>(values[0])");
                builder.Line(
                    ": throw new InvalidOperationException($\"Required response header '{name}' was missing.\");"
                );
            });
        });
        builder.Line();

        builder.Line(
            "private static T GetPrefixedHeaders<T>(IReadOnlyDictionary<string, IReadOnlyList<string>> headers, string prefix)"
        );
        builder.Block(() =>
        {
            builder.Line("var values = new Dictionary<string, string>(StringComparer.Ordinal);");
            builder.Line("foreach (var header in headers)");
            builder.Block(() =>
            {
                builder.Line(
                    "if (!header.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || header.Value.Count == 0)"
                );
                builder.Block(() =>
                {
                    builder.Line("continue;");
                });
                builder.Line();
                builder.Line("values[header.Key[prefix.Length..]] = header.Value[0];");
            });
            builder.Line();
            builder.Line("return CreateStringMap<T>(values);");
        });
        builder.Line();

        builder.Line(
            "private static T GetRequiredPrefixedHeaders<T>(IReadOnlyDictionary<string, IReadOnlyList<string>> headers, string prefix)"
        );
        builder.Block(() =>
        {
            builder.Line("var values = GetPrefixedHeaders<T>(headers, prefix);");
            builder.Line("if (EqualityComparer<T>.Default.Equals(values, default!))");
            builder.Block(() =>
            {
                builder.Line(
                    "throw new InvalidOperationException($\"Required prefixed response headers '{prefix}' were missing.\");"
                );
            });
            builder.Line();
            builder.Line("return values;");
        });
        builder.Line();

        builder.Line("private static T CreateStringMap<T>(Dictionary<string, string> values)");
        builder.Block(() =>
        {
            builder.Line("if (values.Count == 0)");
            builder.Block(() =>
            {
                builder.Line("return default!;");
            });
            builder.Line();
            builder.Line("var targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);");
            builder.Line("if (targetType.IsAssignableFrom(values.GetType()))");
            builder.Block(() =>
            {
                builder.Line("return (T)(object)values;");
            });
            builder.Line();
            builder.Line(
                "var constructor = targetType.GetConstructor([typeof(IReadOnlyDictionary<string, string>)]);"
            );
            builder.Line("if (constructor is null)");
            builder.Block(() =>
            {
                builder.Line(
                    "constructor = targetType.GetConstructor([typeof(Dictionary<string, string>)]);"
                );
            });
            builder.Line();
            builder.Line("return constructor is not null");
            builder.Indented(() =>
            {
                builder.Line("? (T)constructor.Invoke([values])");
                builder.Line(
                    ": throw new InvalidOperationException($\"Cannot create string map type '{targetType}'.\");"
                );
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
            builder.Line("if (targetType == typeof(DateTimeOffset))");
            builder.Block(() =>
            {
                builder.Line(
                    "if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var epochSeconds))"
                );
                builder.Block(() =>
                {
                    builder.Line(
                        "return (T)(object)DateTimeOffset.FromUnixTimeSeconds(epochSeconds);"
                    );
                });
                builder.Line();
                builder.Line(
                    "return (T)(object)DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);"
                );
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

    private static string GetDeserializeBodyMemberExpression(ModelShape service, bool required)
    {
        if (IsRpcV2CborService(service))
        {
            return $"return {GetDeserializeBodyMemberCall(service)};";
        }

        if (IsRestXmlService(service))
        {
            return required
                ? "return DeserializeRequiredXmlBodyMember<T>(content, name);"
                : "return DeserializeXmlBodyMember<T>(content, name);";
        }

        return required
            ? "return DeserializeRequiredJsonBodyMember<T>(content, name);"
            : "return DeserializeJsonBodyMember<T>(content, name);";
    }

    private static string GetDeserializeBodyMemberCall(ModelShape service)
    {
        if (IsRpcV2CborService(service))
        {
            return "SmithyCborPayloadCodec.DeserializeMember<T>(content, name)";
        }

        if (IsRestXmlService(service))
        {
            return "DeserializeXmlBodyMember<T>(content, name)";
        }

        return "DeserializeJsonBodyMember<T>(content, name)";
    }
}

#pragma warning restore CA1305
