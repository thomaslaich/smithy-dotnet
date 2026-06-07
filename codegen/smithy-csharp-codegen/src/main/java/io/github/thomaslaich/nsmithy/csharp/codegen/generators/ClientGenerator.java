/*
 * Renders the C# client(s) for a service.
 *
 * For services with one of the supported HTTP protocols
 * (alloy#simpleRestJson, aws.protocols#restJson1, aws.protocols#restXml,
 * smithy.protocols#rpcv2Cbor) emits an `I{Service}Client` interface and a
 * concrete `{Service}Client` class using SmithyOperationInvoker + the matching
 * protocol runtime helper class.
 *
 * Per-protocol differences (codec, runtime helper namespace, error dispatch,
 * URI scheme, document-vs-binding body handling) are captured in
 * {@link ProtocolSupport}.
 *
 * For @grpc services additionally emits a `{Service}GrpcClient` that wraps the
 * protoc-generated client (expected at
 * `{namespace}.Grpc.{Service}.{Service}Client`) and converts shapes via
 * {@link GrpcConversions}.
 */
package io.github.thomaslaich.nsmithy.csharp.codegen.generators;

import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpNaming;
import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpSymbolProvider;
import io.github.thomaslaich.nsmithy.csharp.codegen.GenerationContext;
import io.github.thomaslaich.nsmithy.csharp.codegen.RuntimeTypes;
import io.github.thomaslaich.nsmithy.csharp.codegen.TraitIds;
import io.github.thomaslaich.nsmithy.csharp.codegen.support.ProtocolSupport;
import io.github.thomaslaich.nsmithy.csharp.codegen.support.ProtocolSupport.Kind;
import io.github.thomaslaich.nsmithy.csharp.codegen.support.ShapeSupport;
import io.github.thomaslaich.nsmithy.csharp.codegen.writer.CSharpWriter;
import java.util.ArrayList;
import java.util.Comparator;
import java.util.HashSet;
import java.util.List;
import java.util.Optional;
import java.util.Set;
import java.util.stream.Collectors;
import software.amazon.smithy.codegen.core.SymbolProvider;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.knowledge.TopDownIndex;
import software.amazon.smithy.model.node.Node;
import software.amazon.smithy.model.shapes.MemberShape;
import software.amazon.smithy.model.shapes.OperationShape;
import software.amazon.smithy.model.shapes.ServiceShape;
import software.amazon.smithy.model.shapes.Shape;
import software.amazon.smithy.model.shapes.ShapeId;
import software.amazon.smithy.model.shapes.ShapeType;
import software.amazon.smithy.model.shapes.StructureShape;
import software.amazon.smithy.model.traits.HttpHeaderTrait;
import software.amazon.smithy.model.traits.HttpPrefixHeadersTrait;
import software.amazon.smithy.model.traits.HttpQueryTrait;
import software.amazon.smithy.model.traits.HttpTrait;
import software.amazon.smithy.model.traits.IdempotencyTokenTrait;
import software.amazon.smithy.model.traits.InputTrait;
import software.amazon.smithy.utils.SmithyInternalApi;

@SmithyInternalApi
public final class ClientGenerator implements Runnable {

  private final GenerationContext context;
  private final CSharpWriter writer;
  private final ServiceShape service;
  private final boolean emitsHttp;
  private final boolean emitsGrpc;
  private final Kind kind;
  private final String runtime; // ProtocolSupport.runtimeProtocolType(kind)
  private final boolean rawRestJsonStringPayloads;

  public ClientGenerator(GenerationContext c, CSharpWriter w, ServiceShape s) {
    this.context = c;
    this.writer = w;
    this.service = s;
    this.emitsHttp = ProtocolSupport.emitsHttpClient(s);
    this.emitsGrpc = ProtocolSupport.isGrpcService(s);
    this.kind = emitsHttp ? ProtocolSupport.kindOf(s) : Kind.REST_JSON;
    this.runtime = ProtocolSupport.runtimeProtocolType(this.kind);
    this.rawRestJsonStringPayloads = s.findTrait(TraitIds.REST_JSON_1).isPresent();
  }

  @Override
  public void run() {
    SymbolProvider sp = context.symbolProvider();
    Model model = context.model();
    TopDownIndex idx = TopDownIndex.of(model);
    List<OperationShape> operations =
        idx.getContainedOperations(service).stream()
            .sorted(Comparator.comparing(o -> o.getId().toString()))
            .collect(Collectors.toList());

    writer.addImport(RuntimeTypes.NSMITHY_CORE);

    String typeName = CSharpNaming.typeName(service.getId().getName()) + "Client";
    String interfaceName = "I" + typeName;

    // Interface
    writer.write("public interface $L", interfaceName);
    writer.openBlock(
        "{",
        "}",
        () -> {
          writer.write("");
          for (OperationShape op : operations) {
            writer.write("$L;", operationSignature(sp, op));
          }
        });
    writer.write("");

    if (emitsHttp) {
      writer.addImport(RuntimeTypes.NSMITHY_CLIENT);
      writer.addImport(RuntimeTypes.NSMITHY_HTTP);
      writer.addImport(RuntimeTypes.NSMITHY_CORE_FUNCTIONAL);
      writer.addImport(ProtocolSupport.runtimeProtocolNamespace(kind));
      writer.addImport(ProtocolSupport.codecNamespace(kind));
      writeHttpClient(sp, model, operations, typeName, interfaceName);
      writer.write("");
    }

    if (emitsGrpc) {
      writer.addImport(RuntimeTypes.GRPC_CORE);
      writeGrpcClient(sp, model, operations, interfaceName);
    }
  }

  // =====================================================================
  // HTTP client
  // =====================================================================

  private void writeHttpClient(
      SymbolProvider sp,
      Model model,
      List<OperationShape> operations,
      String typeName,
      String interfaceName) {
    writer.write("public sealed class $L : $L", typeName, interfaceName);
    writer.openBlock(
        "{",
        "}",
        () -> {
          if (usesLegacyDocumentCodec()) {
            writer.addImport(RuntimeTypes.NSMITHY_CORE_SERDE);
            writer.write(
                "private static readonly $L DocumentCodec = $L;",
                ProtocolSupport.codecType(kind),
                ProtocolSupport.codecExpression(kind));
          }
          writer.write("private readonly SmithyOperationInvoker invoker;");
          writer.write("private readonly SmithyClientOptions options;");
          writer.write("");
          writer.write("public $L(System.Uri endpoint)", typeName);
          writer.write(
              "    : this(new System.Net.Http.HttpClient(), new SmithyClientOptions { Endpoint ="
                  + " endpoint })");
          writer.write("{ }");
          writer.write("");
          writer.write("public $L(System.Net.Http.HttpClient httpClient)", typeName);
          writer.write("    : this(httpClient, SmithyClientOptions.Default)");
          writer.write("{ }");
          writer.write("");
          writer.write(
              "public $L(System.Net.Http.HttpClient httpClient, SmithyClientOptions options)",
              typeName);
          writer.write(
              "    : this(new SmithyOperationInvoker(new HttpClientTransport(httpClient, (options"
                  + " ?? throw new System.ArgumentNullException(nameof(options))).Endpoint),"
                  + " options.Middleware), options)");
          writer.write("{ }");
          writer.write("");
          writer.write("public $L(SmithyOperationInvoker invoker)", typeName);
          writer.write("    : this(invoker, SmithyClientOptions.Default)");
          writer.write("{ }");
          writer.write("");
          writer.write(
              "public $L(SmithyOperationInvoker invoker, SmithyClientOptions options)", typeName);
          writer.openBlock(
              "{",
              "}",
              () -> {
                writer.write(
                    "this.invoker = invoker ?? throw new"
                        + " System.ArgumentNullException(nameof(invoker));");
                writer.write(
                    "this.options = options ?? throw new"
                        + " System.ArgumentNullException(nameof(options));");
              });
          writer.write("");

          for (OperationShape op : operations) writeOperationMethod(sp, model, op);
          for (OperationShape op : operations) writeErrorDeserializer(sp, model, op);

          writeBodyProjectionTypes(sp, model, operations);
        });
  }

  // ---------------- per-operation method ----------------

  private void writeOperationMethod(SymbolProvider sp, Model model, OperationShape op) {
    if (kind == Kind.REST_JSON) {
      writeFunctionalRestJsonOperationMethod(sp, model, op);
      return;
    }

    StructureShape input =
        ShapeSupport.isUnit(op.getInputShape())
            ? null
            : model.expectShape(op.getInputShape(), StructureShape.class);
    StructureShape output =
        ShapeSupport.isUnit(op.getOutputShape())
            ? null
            : model.expectShape(op.getOutputShape(), StructureShape.class);
    boolean rpc = kind == Kind.RPC_V2_CBOR;
    boolean useDoc = ProtocolSupport.useDocumentBindings(kind);
    String method;
    String uri;
    if (rpc) {
      method = "POST";
      uri = "/service/" + service.getId().getName() + "/operation/" + op.getId().getName();
    } else {
      HttpTrait http = op.expectTrait(HttpTrait.class);
      method = http.getMethod();
      uri = trimTrailingSlash(http.getUri().toString());
    }
    String opName = CSharpNaming.typeName(op.getId().getName());
    String deserName = "Deserialize" + opName + "ErrorAsync";

    writer.write("public async $L", operationSignature(sp, op));
    writer.openBlock(
        "{",
        "}",
        () -> {
          if (input != null) writer.write("System.ArgumentNullException.ThrowIfNull(input);");

          if (rpc || useDoc) {
            writer.write("var requestUri = $L;", CSharpNaming.formatString(uri));
          } else {
            writeRequestUriBuilder(input, uri);
          }
          writer.write(
              "var request = new SmithyHttpRequest(new System.Net.Http.HttpMethod($L),"
                  + " requestUri);",
              CSharpNaming.formatString(method));

          if (rpc) {
            writer.write("request.Headers[\"Smithy-Protocol\"] = [\"rpc-v2-cbor\"];");
            writer.write("request.Headers[\"Accept\"] = [$L];", mediaTypeLiteral());
          } else if (input != null && !useDoc) {
            writeRequestHeaders(input);
          }
          if (!rpc && !useDoc && output != null) {
            writeAcceptHeader(output);
          }

          // body
          if (rpc || useDoc) {
            if (input != null) {
              writer.write(
                  "request.Content = $L;", serializeDocumentBodyExpression(input, "input"));
              writer.write("request.ContentType = $L;", mediaTypeLiteral());
            }
          } else {
            Optional<MemberShape> payload =
                input == null
                    ? Optional.empty()
                    : input.members().stream().filter(ShapeSupport::isHttpPayload).findFirst();
            if (payload.isPresent()) {
              MemberShape pm = payload.get();
              String prop = CSharpNaming.propertyName(pm.getMemberName());
              String defaultExpr = ShapeSupport.defaultValueExpression(model, sp, pm);
              if (defaultExpr != null) {
                // alloy semantics: omit the body when the user-provided value equals the
                // member's @default. Mirrors the SimpleRestJsonNoneHttpPayloadWithDefault tests.
                if (ShapeSupport.isNullable(pm)) {
                  writer.write("if (input.$L is { } payloadValue)", prop);
                  writer.openBlock(
                      "{",
                      "}",
                      () -> {
                        writer.write(
                            "if (!System.Collections.Generic.EqualityComparer<$L>.Default.Equals(payloadValue,"
                                + " $L))",
                            ShapeSupport.parameterTypeExpr(sp, pm),
                            defaultExpr);
                        writer.openBlock(
                            "{",
                            "}",
                            () -> {
                              writer.write(
                                  "request.Content = $L;",
                                  serializePayloadExpression(pm, "payloadValue"));
                              writePayloadContentTypeAssignment(input, pm);
                            });
                      });
                } else {
                  writer.write(
                      "if (!System.Collections.Generic.EqualityComparer<$L>.Default.Equals(input.$L,"
                          + " $L))",
                      ShapeSupport.parameterTypeExpr(sp, pm),
                      prop,
                      defaultExpr);
                  writer.openBlock(
                      "{",
                      "}",
                      () -> {
                        writer.write(
                            "request.Content = $L;",
                            serializePayloadExpression(pm, "input." + prop));
                        writePayloadContentTypeAssignment(input, pm);
                      });
                }
              } else {
                if (ShapeSupport.isNullable(pm)) {
                  writer.write("if (input.$L is { } payloadValue)", prop);
                  writer.openBlock(
                      "{",
                      "}",
                      () -> {
                        writer.write(
                            "request.Content = $L;",
                            serializePayloadExpression(pm, "payloadValue"));
                        writePayloadContentTypeAssignment(input, pm);
                      });
                  if (isStructurePayload(pm)) {
                    writer.write("else");
                    writer.openBlock(
                        "{",
                        "}",
                        () -> {
                          writer.write("request.Content = $L;", emptyPayloadExpression(pm));
                          writePayloadContentTypeAssignment(input, pm);
                        });
                  }
                } else {
                  writer.write(
                      "request.Content = $L;", serializePayloadExpression(pm, "input." + prop));
                  writePayloadContentTypeAssignment(input, pm);
                }
              }
            } else if (input != null && hasHttpBody(input)) {
              writeRequestBody(input);
            }
          }
          if (op.findTrait(TraitIds.REQUEST_COMPRESSION).isPresent()) {
            writer.write(
                "$L.ApplyRequestCompression(request, $L);",
                runtime,
                requestCompressionEncoding(op));
          }
          if (op.findTrait(TraitIds.HTTP_CHECKSUM_REQUIRED).isPresent()) {
            writer.write("$L.ApplyContentMd5(request);", runtime);
          }
          writer.write("");
          writer.write(
              "var response = await invoker.InvokeAsync($L, $L, request, $L,"
                  + " cancellationToken).ConfigureAwait(false);",
              CSharpNaming.formatString(service.getId().getName()),
              CSharpNaming.formatString(op.getId().getName()),
              deserName);

          if (output == null) {
            writer.write("return;");
          } else {
            writer.write("");
            writeResponseReturn(sp, output);
          }
        });
    writer.write("");
  }

  private void writeFunctionalRestJsonOperationMethod(
      SymbolProvider sp, Model model, OperationShape op) {
    boolean hasInput = !ShapeSupport.isUnit(op.getInputShape());
    boolean hasOutput = !ShapeSupport.isUnit(op.getOutputShape());
    String opName = CSharpNaming.typeName(op.getId().getName());
    String deserName = "Deserialize" + opName + "ErrorAsync";

    writer.write("public async $L", operationSignature(sp, op));
    writer.openBlock(
        "{",
        "}",
        () -> {
          if (hasInput) {
            writer.write("System.ArgumentNullException.ThrowIfNull(input);");
          }

          writer.write(
              "var request = FunctionalRestJsonProtocol.SerializeRequest($L, $L);",
              SchemaGenerator.functionalOperationSchemaAccessor(context, op),
              hasInput ? "input" : "SmithyUnit.Value");

          if (op.findTrait(TraitIds.REQUEST_COMPRESSION).isPresent()) {
            writer.write(
                "FunctionalRestJsonProtocol.ApplyRequestCompression(request, $L);",
                requestCompressionEncoding(op));
          }
          if (op.findTrait(TraitIds.HTTP_CHECKSUM_REQUIRED).isPresent()) {
            writer.write("FunctionalRestJsonProtocol.ApplyContentMd5(request);");
          }

          writer.write("");
          writer.write(
              "var response = await invoker.InvokeAsync($L, $L, request, $L,"
                  + " cancellationToken).ConfigureAwait(false);",
              CSharpNaming.formatString(service.getId().getName()),
              CSharpNaming.formatString(op.getId().getName()),
              deserName);

          if (hasOutput) {
            writer.write("");
            writer.write(
                "return FunctionalRestJsonProtocol.DeserializeResponse($L," + " response);",
                SchemaGenerator.functionalOperationSchemaAccessor(context, op));
          } else {
            writer.write("return;");
          }
        });
    writer.write("");
  }

  private boolean usesLegacyDocumentCodec() {
    return kind == Kind.REST_JSON;
  }

  private boolean usesFunctionalDocumentCodec() {
    return kind == Kind.REST_XML || kind == Kind.RPC_V2_CBOR;
  }

  private String mediaTypeLiteral() {
    return CSharpNaming.formatString(ProtocolSupport.mediaType(kind));
  }

  private String codecFromSchema(Shape shape) {
    return switch (kind) {
      case REST_XML ->
          "FunctionalXmlCodec.FromSchema("
              + SchemaGenerator.functionalShapeSchemaAccessor(context, shape)
              + ")";
      case RPC_V2_CBOR ->
          "FunctionalCborCodec.FromSchema("
              + SchemaGenerator.functionalShapeSchemaAccessor(context, shape)
              + ")";
      case REST_JSON -> "DocumentCodec";
    };
  }

  private String serializeDocumentBodyExpression(Shape shape, String valueExpr) {
    String codec = codecFromSchema(shape);
    return switch (kind) {
      case REST_XML ->
          "System.Text.Encoding.UTF8.GetBytes(" + codec + ".Serialize(" + valueExpr + "))";
      case RPC_V2_CBOR -> codec + ".Serialize(" + valueExpr + ")";
      case REST_JSON -> "DocumentCodec.Serialize(" + valueExpr + ")";
    };
  }

  private void writeRequestUriBuilder(StructureShape input, String uri) {
    writer.write(
        "var requestUriBuilder = new System.Text.StringBuilder($L);",
        CSharpNaming.formatString(uri));
    if (input != null) {
      for (MemberShape m : ShapeSupport.sortedMembers(input)) {
        if (!ShapeSupport.isHttpLabel(m)) continue;
        String prop = CSharpNaming.propertyName(m.getMemberName());
        String varName = CSharpNaming.parameterName(m.getMemberName()) + "Label";
        if (ShapeSupport.isReferenceType(context.model(), m)) {
          writer.write(
              "var $L = input.$L ?? throw new System.ArgumentException($L, nameof(input));",
              varName,
              prop,
              CSharpNaming.formatString("HTTP label '" + m.getMemberName() + "' is required."));
        } else {
          writer.write("var $L = input.$L;", varName, prop);
        }
        writer.write(
            "requestUriBuilder.Replace($L, $L.EscapeGreedyLabel($L, $L));",
            CSharpNaming.formatString("{" + m.getMemberName() + "+}"),
            runtime,
            SchemaGenerator.memberSchemaExpr(context, m),
            varName);
        writer.write(
            "requestUriBuilder.Replace($L,"
                + " System.Uri.EscapeDataString($L.FormatHttpValue($L, $L)));",
            CSharpNaming.formatString("{" + m.getMemberName() + "}"),
            runtime,
            SchemaGenerator.memberSchemaExpr(context, m),
            varName);
      }
      for (MemberShape m : ShapeSupport.sortedMembers(input)) {
        if (!ShapeSupport.isHttpQuery(m)) continue;
        String qn = m.expectTrait(HttpQueryTrait.class).getValue();
        String prop = CSharpNaming.propertyName(m.getMemberName());
        if (m.hasTrait(IdempotencyTokenTrait.class)) {
          String local = CSharpNaming.parameterName(m.getMemberName()) + "QueryValue";
          writer.write("var $L = input.$L ?? options.IdempotencyTokenProvider();", local, prop);
          writer.write(
              "$L.AppendQuery(requestUriBuilder, $L, $L, $L);",
              runtime,
              CSharpNaming.formatString(qn),
              SchemaGenerator.memberSchemaExpr(context, m),
              local);
        } else {
          writer.write(
              "$L.AppendQuery(requestUriBuilder, $L, $L, input.$L);",
              runtime,
              CSharpNaming.formatString(qn),
              SchemaGenerator.memberSchemaExpr(context, m),
              prop);
        }
      }
      List<String> explicitQueryNames =
          ShapeSupport.sortedMembers(input).stream()
              .filter(ShapeSupport::isHttpQuery)
              .map(m -> m.expectTrait(HttpQueryTrait.class).getValue())
              .sorted()
              .collect(Collectors.toList());
      for (MemberShape m : ShapeSupport.sortedMembers(input)) {
        if (!ShapeSupport.isHttpQueryParams(m)) continue;
        if (explicitQueryNames.isEmpty()) {
          writer.write(
              "$L.AppendQueryMap(requestUriBuilder, input.$L);",
              runtime,
              CSharpNaming.propertyName(m.getMemberName()));
        } else {
          writer.write(
              "$L.AppendQueryMap(requestUriBuilder, input.$L, new string[] { $L });",
              runtime,
              CSharpNaming.propertyName(m.getMemberName()),
              explicitQueryNames.stream()
                  .map(CSharpNaming::formatString)
                  .collect(Collectors.joining(", ")));
        }
      }
    }
    writer.write("var requestUri = requestUriBuilder.ToString();");
  }

  private void writeRequestHeaders(StructureShape input) {
    for (MemberShape m : ShapeSupport.sortedMembers(input)) {
      if (ShapeSupport.isHttpHeader(m)) {
        String name = m.expectTrait(HttpHeaderTrait.class).getValue();
        if ("Content-Type".equalsIgnoreCase(name)) {
          writer.write("if (input.$L is { } value)", CSharpNaming.propertyName(m.getMemberName()));
          writer.openBlock("{", "}", () -> writer.write("request.ContentType = value;"));
        } else if ("Content-Encoding".equalsIgnoreCase(name)) {
          writer.write(
              "$L.AddHeader(request.ContentHeaders, $L, $L, input.$L);",
              runtime,
              CSharpNaming.formatString(name),
              SchemaGenerator.memberSchemaExpr(context, m),
              CSharpNaming.propertyName(m.getMemberName()));
        } else {
          writer.write(
              "$L.AddHeader(request.Headers, $L, $L, input.$L);",
              runtime,
              CSharpNaming.formatString(name),
              SchemaGenerator.memberSchemaExpr(context, m),
              CSharpNaming.propertyName(m.getMemberName()));
        }
      } else if (ShapeSupport.isHttpPrefixHeaders(m)) {
        String prefix = m.expectTrait(HttpPrefixHeadersTrait.class).getValue();
        writer.write(
            "$L.AddPrefixedHeaders(request.Headers, $L, input.$L);",
            runtime,
            CSharpNaming.formatString(prefix),
            CSharpNaming.propertyName(m.getMemberName()));
      }
    }
  }

  private boolean hasHttpBody(StructureShape input) {
    return input.members().stream().anyMatch(ShapeSupport::isHttpBody);
  }

  private void writeRequestBody(StructureShape input) {
    List<MemberShape> bodyMembers =
        input.members().stream().filter(ShapeSupport::isHttpBody).collect(Collectors.toList());
    if (bodyMembers.isEmpty()) return;
    String bodyType = bodyProjectionName(input);
    writer.openBlock(
        "var requestBody = new $L(",
        ");",
        bodyType,
        () -> {
          for (int i = 0; i < bodyMembers.size(); i++) {
            String prop = CSharpNaming.propertyName(bodyMembers.get(i).getMemberName());
            writer.write("input.$L$L", prop, i == bodyMembers.size() - 1 ? "" : ",");
          }
        });
    writer.write("request.Content = DocumentCodec.Serialize(requestBody);");
    writer.write("request.ContentType = DocumentCodec.MediaType;");
  }

  private void writeResponseReturn(SymbolProvider sp, StructureShape output) {
    boolean useDoc = ProtocolSupport.useDocumentBindings(kind) || kind == Kind.RPC_V2_CBOR;
    if (useDoc || !hasResponseBindings(output)) {
      String outputType = CSharpSymbolProvider.qualified(sp.toSymbol(output));
      if (usesFunctionalDocumentCodec()) {
        writer.write(
            "return $L.DeserializeRequiredBody($L, response.Content);",
            runtime,
            codecFromSchema(output));
      } else {
        writer.write(
            "return $L.DeserializeRequiredBody<$L>(DocumentCodec, response.Content);",
            runtime,
            outputType);
      }
      return;
    }
    List<MemberShape> bodyMembers = responseBodyMembers(output);
    String bodyVar = null;
    if (!bodyMembers.isEmpty()) {
      String bodyType = bodyProjectionName(output);
      boolean requiresBody = bodyMembers.stream().anyMatch(ShapeSupport::isRequired);
      writer.write(
          "var body = $L.$L<$L>(DocumentCodec, response.Content);",
          runtime,
          requiresBody ? "DeserializeRequiredBody" : "DeserializeBody",
          bodyType);
      writer.write("");
      bodyVar = "body";
    }
    String outputType = CSharpSymbolProvider.qualified(sp.toSymbol(output));
    List<MemberShape> ctor = ShapeSupport.constructorMembers(output);
    final String bv = bodyVar;
    writer.openBlock(
        "return new $L(",
        ");",
        outputType,
        () -> {
          for (int i = 0; i < ctor.size(); i++) {
            writer.write(
                "$L$L",
                responseMemberExpression(sp, ctor.get(i), bv),
                i == ctor.size() - 1 ? "" : ",");
          }
        });
  }

  private String responseMemberExpression(SymbolProvider sp, MemberShape m, String bodyVar) {
    boolean required = ShapeSupport.isRequired(m);
    String memberType = ShapeSupport.parameterTypeExpr(sp, m);
    if (ShapeSupport.isHttpHeader(m)) {
      String name = m.expectTrait(HttpHeaderTrait.class).getValue();
      return required
          ? runtime
              + ".GetRequiredHeader<"
              + memberType
              + ">(response.Headers, "
              + CSharpNaming.formatString(name)
              + ", "
              + SchemaGenerator.memberSchemaExpr(context, m)
              + ")"
          : runtime
              + ".GetHeader<"
              + memberType
              + ">(response.Headers, "
              + CSharpNaming.formatString(name)
              + ", "
              + SchemaGenerator.memberSchemaExpr(context, m)
              + ")";
    }
    if (ShapeSupport.isHttpPrefixHeaders(m)) {
      String prefix = m.expectTrait(HttpPrefixHeadersTrait.class).getValue();
      return required
          ? runtime
              + ".GetRequiredPrefixedHeaders<"
              + memberType
              + ">(response.Headers, "
              + CSharpNaming.formatString(prefix)
              + ")"
          : runtime
              + ".GetPrefixedHeaders<"
              + memberType
              + ">(response.Headers, "
              + CSharpNaming.formatString(prefix)
              + ")";
    }
    if (ShapeSupport.isHttpResponseCode(m)) {
      return "(" + memberType + ")(int)response.StatusCode";
    }
    if (ShapeSupport.isHttpPayload(m)) {
      // When the member has @default, fall back to non-required deserialization so an empty
      // body returns null and the output ctor substitutes the default value.
      boolean hasDefault = ShapeSupport.hasDefault(m);
      return deserializePayloadExpression(m, required && !hasDefault);
    }
    if (bodyVar != null) {
      return bodyVar + "." + CSharpNaming.propertyName(m.getMemberName());
    }
    throw new RuntimeException("Body member without projection: " + m.getId());
  }

  // ---------------- error deserializer ----------------

  private void writeErrorDeserializer(SymbolProvider sp, Model model, OperationShape op) {
    String opName = CSharpNaming.typeName(op.getId().getName());
    String methodName = "Deserialize" + opName + "ErrorAsync";
    List<ShapeId> errorIds = new ArrayList<>(op.getErrors(service));
    errorIds.sort(Comparator.comparing(ShapeId::toString));

    writer.write(
        "private static System.Threading.Tasks.ValueTask<System.Exception?> $L(SmithyHttpResponse"
            + " response, System.Threading.CancellationToken cancellationToken)",
        methodName);
    writer.openBlock(
        "{",
        "}",
        () -> {
          if (errorIds.isEmpty() || kind == Kind.REST_JSON) {
            writer.write("");
            writer.write(
                "return System.Threading.Tasks.ValueTask.FromResult<System.Exception?>(null);");
            return;
          }
          switch (kind) {
            case RPC_V2_CBOR -> {
              writer.write("if (!$L.HasResponse(response))", runtime);
              writer.openBlock(
                  "{",
                  "}",
                  () ->
                      writer.write(
                          "return"
                              + " System.Threading.Tasks.ValueTask.FromResult<System.Exception?>(null);"));
              writer.write("");
              writer.write("var errorType = $L.DeserializeErrorType(response.Content);", runtime);
              for (ShapeId errId : errorIds) {
                StructureShape err = model.expectShape(errId, StructureShape.class);
                writer.write("");
                writer.write(
                    "if (string.Equals(errorType, $L, System.StringComparison.Ordinal)"
                        + " || string.Equals(errorType, $L, System.StringComparison.Ordinal))",
                    CSharpNaming.formatString(errId.getName()),
                    CSharpNaming.formatString(errId.toString()));
                writer.openBlock("{", "}", () -> writeErrorReturn(sp, err));
              }
            }
            case REST_XML -> {
              writer.write("var errorType = $L.DeserializeErrorCode(response.Content);", runtime);
              for (ShapeId errId : errorIds) {
                StructureShape err = model.expectShape(errId, StructureShape.class);
                writer.write("");
                writer.write(
                    "if (string.Equals(errorType, $L, System.StringComparison.Ordinal))",
                    CSharpNaming.formatString(errId.getName()));
                writer.openBlock("{", "}", () -> writeErrorReturn(sp, err));
              }
            }
            case REST_JSON -> {
              writer.write("var errorType = $L.DeserializeErrorType(response);", runtime);
              for (ShapeId errId : errorIds) {
                StructureShape err = model.expectShape(errId, StructureShape.class);
                writer.write("");
                writer.write(
                    "if (string.Equals(errorType, $L, System.StringComparison.Ordinal))",
                    CSharpNaming.formatString(errId.getName()));
                writer.openBlock("{", "}", () -> writeErrorReturn(sp, err));
              }
              for (ShapeId errId : errorIds) {
                StructureShape err = model.expectShape(errId, StructureShape.class);
                Integer status = httpErrorCode(err);
                if (status == null) continue;
                writer.write("");
                writer.write("if ((int)response.StatusCode == $L)", status);
                writer.openBlock("{", "}", () -> writeErrorReturn(sp, err));
              }
            }
          }
          // fallback: first error. Wrap in an explicit block so the inner `var errorBody`
          // doesn't collide with the per-status branches above (CS0136).
          // Guard against empty bodies: if we have nothing to deserialize we cannot
          // recognise any error, so return null and let InvokeAsync throw a generic
          // SmithyClientException instead of crashing with MissingMethodException.
          ShapeId fallback = errorIds.get(0);
          StructureShape err = model.expectShape(fallback, StructureShape.class);
          boolean fallbackHasBody =
              !ProtocolSupport.useDocumentBindings(kind) && !responseBodyMembers(err).isEmpty();
          writer.write("");
          writer.openBlock(
              "{",
              "}",
              () -> {
                if (fallbackHasBody) {
                  writer.write("if (response.Content.Length == 0)");
                  writer.openBlock(
                      "{",
                      "}",
                      () ->
                          writer.write(
                              "return"
                                  + " System.Threading.Tasks.ValueTask.FromResult<System.Exception?>(null);"));
                  writer.write("");
                }
                writeErrorReturn(sp, err);
              });
        });
    writer.write("");
  }

  private String errorConstruction(SymbolProvider sp, StructureShape err, String bodyVar) {
    if (ProtocolSupport.useDocumentBindings(kind)) {
      // Whole error body is a single document.
      if (usesFunctionalDocumentCodec()) {
        return runtime + ".DeserializeRequiredBody(" + codecFromSchema(err) + ", response.Content)";
      }
      String t = CSharpSymbolProvider.qualified(sp.toSymbol(err));
      return runtime + ".DeserializeRequiredBody<" + t + ">(DocumentCodec, response.Content)";
    }
    // Mirror ErrorGenerator's ctor signature: leading `string? message` (always present, even
    // when the shape has no `message` member — that's how System.Exception.Message is wired)
    // followed by the remaining members in constructor order (required first, then optional,
    // each alphabetical) — NOT sortedMembers order, which would mis-align args with parameters.
    Optional<MemberShape> mm = ShapeSupport.errorMessageMember(context.model(), err);
    List<MemberShape> ctor = ShapeSupport.constructorMembers(err, mm.orElse(null));
    StringBuilder sb =
        new StringBuilder("new ")
            .append(CSharpSymbolProvider.qualified(sp.toSymbol(err)))
            .append("(");
    sb.append(mm.isPresent() ? responseMemberExpression(sp, mm.get(), bodyVar) : "null");
    for (MemberShape m : ctor) {
      sb.append(", ").append(responseMemberExpression(sp, m, bodyVar));
    }
    sb.append(")");
    return sb.toString();
  }

  /**
   * Emits the body deserialization (when needed) plus the {@code return
   * ValueTask.FromResult<Exception?>(new ErrorXyz(...));} line for an error shape. For
   * REST_JSON/REST_XML the error body is decoded into the error's body-projection type so that
   * body-bound members can be projected onto the user-facing error constructor arguments.
   */
  private void writeErrorReturn(SymbolProvider sp, StructureShape err) {
    String bodyVar = null;
    if (!ProtocolSupport.useDocumentBindings(kind)) {
      List<MemberShape> bodyMembers = responseBodyMembers(err);
      if (!bodyMembers.isEmpty()) {
        String bodyType = bodyProjectionName(err);
        boolean requiresBody = bodyMembers.stream().anyMatch(ShapeSupport::isRequired);
        writer.write(
            "var errorBody = $L.$L<$L>(DocumentCodec, response.Content);",
            runtime,
            requiresBody ? "DeserializeRequiredBody" : "DeserializeBody",
            bodyType);
        bodyVar = "errorBody";
      }
    }
    writer.write(
        "return System.Threading.Tasks.ValueTask.FromResult<System.Exception?>($L);",
        errorConstruction(sp, err, bodyVar));
  }

  private static Integer httpErrorCode(StructureShape err) {
    Integer explicit =
        err.getTrait(software.amazon.smithy.model.traits.HttpErrorTrait.class)
            .map(t -> t.getCode())
            .orElse(null);
    if (explicit != null) return explicit;
    // Smithy default: @error("client") → 400, @error("server") → 500.
    return err.getTrait(software.amazon.smithy.model.traits.ErrorTrait.class)
        .map(t -> t.isClientError() ? 400 : 500)
        .orElse(null);
  }

  private String serializePayloadValue(MemberShape member, String serializerVar, String valueExpr) {
    Shape target = context.model().expectShape(member.getTarget());
    return writeValueStatement(
        target, serializerVar, SchemaGenerator.memberSchemaExpr(context, member), valueExpr);
  }

  private String deserializePayloadValue(MemberShape member, String deserializerVar) {
    Shape target = context.model().expectShape(member.getTarget());
    return readValueExpression(
        target, deserializerVar, SchemaGenerator.memberSchemaExpr(context, member));
  }

  private String serializePayloadExpression(MemberShape member, String valueExpr) {
    Shape target = context.model().expectShape(member.getTarget());
    if (target.getType() == ShapeType.BLOB) {
      return valueExpr;
    }
    if (rawRestJsonStringPayloads && target.getType() == ShapeType.STRING) {
      return "System.Text.Encoding.UTF8.GetBytes(" + valueExpr + ")";
    }
    if (rawRestJsonStringPayloads && target.getType() == ShapeType.ENUM) {
      return "System.Text.Encoding.UTF8.GetBytes(" + valueExpr + ".ToString())";
    }
    if (ShapeSupport.usesShapeSerde(target)) {
      return "DocumentCodec.Serialize(" + valueExpr + ")";
    }
    return "DocumentCodec.Serialize(serializer => "
        + stripTrailingSemicolon(serializePayloadValue(member, "serializer", valueExpr))
        + ")";
  }

  private String deserializePayloadExpression(MemberShape member, boolean required) {
    Shape target = context.model().expectShape(member.getTarget());
    if (target.getType() == ShapeType.BLOB) {
      return required
          ? "response.Content"
          : "response.Content.Length == 0 ? null : response.Content";
    }
    if (rawRestJsonStringPayloads && target.getType() == ShapeType.STRING) {
      return required
          ? "System.Text.Encoding.UTF8.GetString(response.Content)"
          : "response.Content.Length == 0 ? null :"
              + " System.Text.Encoding.UTF8.GetString(response.Content)";
    }
    if (rawRestJsonStringPayloads && target.getType() == ShapeType.ENUM) {
      String type = CSharpSymbolProvider.qualified(context.symbolProvider().toSymbol(target));
      return required
          ? "new " + type + "(System.Text.Encoding.UTF8.GetString(response.Content))"
          : "response.Content.Length == 0 ? null : new "
              + type
              + "(System.Text.Encoding.UTF8.GetString(response.Content))";
    }
    if (ShapeSupport.usesShapeSerde(target)) {
      String type = CSharpSymbolProvider.qualified(context.symbolProvider().toSymbol(target));
      return required
          ? runtime + ".DeserializeRequiredBody<" + type + ">(DocumentCodec, response.Content)"
          : runtime + ".DeserializeBody<" + type + ">(DocumentCodec, response.Content)";
    }
    return required
        ? runtime
            + ".DeserializeRequiredBody(DocumentCodec, response.Content, reader => "
            + deserializePayloadValue(member, "reader")
            + ")"
        : runtime
            + ".DeserializeBody(DocumentCodec, response.Content, reader => "
            + deserializePayloadValue(member, "reader")
            + ")";
  }

  private String payloadContentType(MemberShape member) {
    Shape target = context.model().expectShape(member.getTarget());
    String explicitMediaType = mediaTypeValue(target);
    if (explicitMediaType != null) {
      return CSharpNaming.formatString(explicitMediaType);
    }
    return switch (target.getType()) {
      case BLOB -> CSharpNaming.formatString("application/octet-stream");
      case STRING, ENUM ->
          rawRestJsonStringPayloads
              ? CSharpNaming.formatString("text/plain")
              : "DocumentCodec.MediaType";
      default -> "DocumentCodec.MediaType";
    };
  }

  private static String stripTrailingSemicolon(String statement) {
    return statement.endsWith(";") ? statement.substring(0, statement.length() - 1) : statement;
  }

  private boolean isBodyProjectionNullable(StructureShape shape, MemberShape member) {
    return ShapeSupport.isNullable(member)
        || (shape.hasTrait(InputTrait.class) && ShapeSupport.hasDefault(member));
  }

  private boolean isStructurePayload(MemberShape member) {
    Shape target = context.model().expectShape(member.getTarget());
    return kind == Kind.REST_JSON && target.getType() == ShapeType.STRUCTURE;
  }

  private String emptyPayloadExpression(MemberShape member) {
    String type =
        CSharpSymbolProvider.qualified(
            context.symbolProvider().toSymbol(context.model().expectShape(member.getTarget())));
    return "DocumentCodec.Serialize(new " + type + "())";
  }

  private void writePayloadContentTypeAssignment(StructureShape input, MemberShape payload) {
    String contentType = payloadContentType(payload, input);
    if (contentType != null) {
      writer.write("request.ContentType = $L;", contentType);
    }
  }

  private void writeAcceptHeader(StructureShape output) {
    String acceptType = acceptType(output);
    if (acceptType != null) {
      writer.write("request.Headers[\"Accept\"] = [$L];", acceptType);
    }
  }

  private String acceptType(StructureShape output) {
    if (usesFunctionalDocumentCodec()) {
      return mediaTypeLiteral();
    }

    return output.members().stream()
        .filter(ShapeSupport::isHttpPayload)
        .findFirst()
        .map(this::payloadContentType)
        .orElse("DocumentCodec.MediaType");
  }

  private String payloadContentType(MemberShape payload, StructureShape input) {
    return hasExplicitContentTypeHeader(input) ? null : payloadContentType(payload);
  }

  private boolean hasExplicitContentTypeHeader(StructureShape input) {
    return input.members().stream()
        .filter(ShapeSupport::isHttpHeader)
        .map(m -> m.expectTrait(HttpHeaderTrait.class).getValue())
        .anyMatch("Content-Type"::equalsIgnoreCase);
  }

  private String requestCompressionEncoding(OperationShape op) {
    return op.findTrait(TraitIds.REQUEST_COMPRESSION)
        .map(t -> (Node) t.toNode())
        .flatMap(
            node ->
                node.expectObjectNode()
                    .getArrayMember("encodings")
                    .flatMap(array -> array.getElements().stream().findFirst())
                    .map(Node::expectStringNode)
                    .map(s -> s.getValue()))
        .map(CSharpNaming::formatString)
        .orElseThrow(
            () ->
                new IllegalStateException(
                    "@requestCompression on "
                        + op.getId()
                        + " has no encodings — trait requires at least one"));
  }

  private String mediaTypeValue(Shape shape) {
    return shape
        .findTrait(TraitIds.MEDIA_TYPE)
        .map(t -> ((Node) t.toNode()).expectStringNode().getValue())
        .orElse(null);
  }

  // ---------------- body projection types ----------------

  private void writeBodyProjectionTypes(SymbolProvider sp, Model model, List<OperationShape> ops) {
    if (ProtocolSupport.useDocumentBindings(kind) || kind == Kind.REST_JSON) {
      // Whole-shape/document protocols and the functional restJson path do not need generated
      // private body DTOs.
      return;
    }
    Set<ShapeId> emitted = new HashSet<>();
    for (OperationShape op : ops) {
      if (!ShapeSupport.isUnit(op.getInputShape()) && emitted.add(op.getInputShape())) {
        StructureShape input = model.expectShape(op.getInputShape(), StructureShape.class);
        List<MemberShape> bodyMembers =
            input.members().stream().filter(ShapeSupport::isHttpBody).collect(Collectors.toList());
        if (!bodyMembers.isEmpty()) writeBodyProjectionType(sp, input, bodyMembers);
      }
      if (!ShapeSupport.isUnit(op.getOutputShape()) && emitted.add(op.getOutputShape())) {
        StructureShape output = model.expectShape(op.getOutputShape(), StructureShape.class);
        if (hasResponseBindings(output)) {
          List<MemberShape> bodyMembers = responseBodyMembers(output);
          if (!bodyMembers.isEmpty()) writeBodyProjectionType(sp, output, bodyMembers);
        }
      }
    }
    for (OperationShape op : ops) {
      for (ShapeId errId : op.getErrors(service)) {
        if (!emitted.add(errId)) continue;
        StructureShape err = model.expectShape(errId, StructureShape.class);
        List<MemberShape> bodyMembers = responseBodyMembers(err);
        if (!bodyMembers.isEmpty()) writeBodyProjectionType(sp, err, bodyMembers);
      }
    }
  }

  private void writeBodyProjectionType(
      SymbolProvider sp, StructureShape shape, List<MemberShape> bodyMembers) {
    String typeName = bodyProjectionName(shape);
    writer.addImport(RuntimeTypes.NSMITHY_CORE_SERDE);
    writer.write(
        "private sealed class $L : ISerializableStruct, IDeserializableShape<$L>",
        typeName,
        typeName);
    writer.openBlock(
        "{",
        "}",
        () -> {
          writer.write("");
          SchemaGenerator.writeStructureSchema(writer, context, shape, bodyMembers);
          writer.write("Schema ISerializableShape.Schema => Schema;");
          writer.write("");
          writer.openBlock(
              "public $L(",
              ")",
              typeName,
              () -> {
                for (int i = 0; i < bodyMembers.size(); i++) {
                  MemberShape m = bodyMembers.get(i);
                  String type =
                      ShapeSupport.memberTypeExpr(sp, m, isBodyProjectionNullable(shape, m));
                  writer.write(
                      "$L $L$L",
                      type,
                      CSharpNaming.parameterName(m.getMemberName()),
                      i == bodyMembers.size() - 1 ? "" : ",");
                }
              });
          writer.write("{");
          writer.indent();
          for (MemberShape m : bodyMembers) {
            String prop = CSharpNaming.propertyName(m.getMemberName());
            String param = CSharpNaming.parameterName(m.getMemberName());
            writer.write("$L = $L;", prop, param);
          }
          writer.dedent();
          writer.write("}");
          writer.write("");
          for (MemberShape m : bodyMembers) {
            String type = ShapeSupport.memberTypeExpr(sp, m, isBodyProjectionNullable(shape, m));
            writer.write(
                "public $L $L { get; }", type, CSharpNaming.propertyName(m.getMemberName()));
            writer.write("");
          }
          writer.write("public void Serialize(IShapeSerializer serializer)");
          writer.openBlock(
              "{",
              "}",
              () -> {
                writer.write("System.ArgumentNullException.ThrowIfNull(serializer);");
                writer.write("serializer.WriteStruct(Schema, this);");
              });
          writer.write("");
          writer.write("public void SerializeMembers(IShapeSerializer serializer)");
          writer.openBlock(
              "{",
              "}",
              () -> {
                for (MemberShape m : bodyMembers) {
                  String prop = CSharpNaming.propertyName(m.getMemberName());
                  String schema = SchemaGenerator.memberSchemaFieldName(m);
                  Shape target = context.model().expectShape(m.getTarget());
                  if (isBodyProjectionNullable(shape, m)) {
                    String local = CSharpNaming.parameterName(m.getMemberName());
                    writer.write("if ($L is { } $L)", prop, local);
                    writer.openBlock(
                        "{",
                        "}",
                        () ->
                            writer.write(writeValueStatement(target, "serializer", schema, local)));
                  } else {
                    writer.write(writeValueStatement(target, "serializer", schema, prop));
                  }
                }
              });
          writer.write("");
          writer.write("public static $L Deserialize(IShapeDeserializer deserializer)", typeName);
          writer.openBlock(
              "{",
              "}",
              () -> {
                writer.write("System.ArgumentNullException.ThrowIfNull(deserializer);");
                for (MemberShape m : bodyMembers) {
                  writer.write(
                      "$L $L = null;",
                      ShapeSupport.memberTypeExpr(sp, m, true),
                      CSharpNaming.parameterName(m.getMemberName()));
                }
                writer.write("");
                writer.write(
                    "deserializer.ReadStruct<object?>(Schema, null, new"
                        + " StructMemberConsumer<object?>(");
                writer.write("Member: (_, field, reader) =>");
                writer.openBlock(
                    "{",
                    "}",
                    () -> {
                      for (int i = 0; i < bodyMembers.size(); i++) {
                        MemberShape m = bodyMembers.get(i);
                        String local = CSharpNaming.parameterName(m.getMemberName());
                        String schema = SchemaGenerator.memberSchemaFieldName(m);
                        Shape target = context.model().expectShape(m.getTarget());
                        String keyword = i == 0 ? "if" : "else if";
                        writer.write(
                            keyword + " (field.MemberName == $L)",
                            CSharpNaming.formatString(m.getMemberName()));
                        writer.openBlock(
                            "{",
                            "}",
                            () -> {
                              if (ShapeSupport.isNullable(m)) {
                                writer.write("if (reader.IsNull())");
                                writer.openBlock(
                                    "{", "}", () -> writer.write("reader.ReadNull();"));
                                writer.write("else");
                                writer.openBlock(
                                    "{",
                                    "}",
                                    () ->
                                        writer.write(
                                            local
                                                + " = "
                                                + readValueExpression(target, "reader", schema)
                                                + ";"));
                              } else {
                                writer.write(
                                    local
                                        + " = "
                                        + readValueExpression(target, "reader", schema)
                                        + ";");
                              }
                            });
                      }
                    });
                writer.write("));");
                writer.write("");
                writer.write(
                    "return new $L($L);",
                    typeName,
                    bodyProjectionConstructorArguments(shape, bodyMembers));
              });
        });
    writer.write("");
  }

  private String bodyProjectionConstructorArguments(
      StructureShape shape, List<MemberShape> bodyMembers) {
    List<String> args = new ArrayList<>();
    for (MemberShape m : bodyMembers) {
      String local = CSharpNaming.parameterName(m.getMemberName());
      String defaultExpr =
          ShapeSupport.defaultValueExpression(context.model(), context.symbolProvider(), m);
      if (defaultExpr != null && !isBodyProjectionNullable(shape, m)) {
        args.add(local + " ?? " + defaultExpr);
      } else if (ShapeSupport.isOptionalParameter(m)) {
        args.add(local);
      } else {
        args.add(
            local
                + " ?? throw new System.InvalidOperationException("
                + CSharpNaming.formatString("Missing required member '" + m.getMemberName() + "'.")
                + ")");
      }
    }
    return String.join(", ", args);
  }

  private String writeValueStatement(
      Shape target, String serializerVar, String schemaVar, String valueExpr) {
    return switch (target.getType()) {
      case BOOLEAN -> serializerVar + ".WriteBoolean(" + schemaVar + ", " + valueExpr + ");";
      case BYTE -> serializerVar + ".WriteByte(" + schemaVar + ", " + valueExpr + ");";
      case SHORT -> serializerVar + ".WriteShort(" + schemaVar + ", " + valueExpr + ");";
      case INTEGER -> serializerVar + ".WriteInteger(" + schemaVar + ", " + valueExpr + ");";
      case LONG -> serializerVar + ".WriteLong(" + schemaVar + ", " + valueExpr + ");";
      case FLOAT -> serializerVar + ".WriteFloat(" + schemaVar + ", " + valueExpr + ");";
      case DOUBLE -> serializerVar + ".WriteDouble(" + schemaVar + ", " + valueExpr + ");";
      case BIG_INTEGER -> serializerVar + ".WriteBigInteger(" + schemaVar + ", " + valueExpr + ");";
      case BIG_DECIMAL -> serializerVar + ".WriteBigDecimal(" + schemaVar + ", " + valueExpr + ");";
      case TIMESTAMP -> serializerVar + ".WriteTimestamp(" + schemaVar + ", " + valueExpr + ");";
      case STRING -> serializerVar + ".WriteString(" + schemaVar + ", " + valueExpr + "!);";
      case ENUM -> serializerVar + ".WriteString(" + schemaVar + ", " + valueExpr + ".Value);";
      case BLOB -> serializerVar + ".WriteBlob(" + schemaVar + ", " + valueExpr + ");";
      case DOCUMENT -> serializerVar + ".WriteDocument(" + schemaVar + ", " + valueExpr + "!);";
      case INT_ENUM -> serializerVar + ".WriteInteger(" + schemaVar + ", (int)" + valueExpr + ");";
      case STRUCTURE -> serializerVar + ".WriteStruct(" + schemaVar + ", " + valueExpr + ");";
      case UNION, LIST, SET, MAP ->
          valueExpr + ".Serialize(" + serializerVar + ", " + schemaVar + ");";
      default ->
          throw new IllegalArgumentException(
              "Unsupported body projection member shape: " + target.getId());
    };
  }

  private String readValueExpression(Shape target, String deserializerVar, String schemaVar) {
    return switch (target.getType()) {
      case BOOLEAN -> deserializerVar + ".ReadBoolean(" + schemaVar + ")";
      case BYTE -> deserializerVar + ".ReadByte(" + schemaVar + ")";
      case SHORT -> deserializerVar + ".ReadShort(" + schemaVar + ")";
      case INTEGER -> deserializerVar + ".ReadInteger(" + schemaVar + ")";
      case LONG -> deserializerVar + ".ReadLong(" + schemaVar + ")";
      case FLOAT -> deserializerVar + ".ReadFloat(" + schemaVar + ")";
      case DOUBLE -> deserializerVar + ".ReadDouble(" + schemaVar + ")";
      case BIG_INTEGER -> deserializerVar + ".ReadBigInteger(" + schemaVar + ")";
      case BIG_DECIMAL -> deserializerVar + ".ReadBigDecimal(" + schemaVar + ")";
      case TIMESTAMP -> deserializerVar + ".ReadTimestamp(" + schemaVar + ")";
      case STRING -> deserializerVar + ".ReadString(" + schemaVar + ")";
      case BLOB -> deserializerVar + ".ReadBlob(" + schemaVar + ")";
      case DOCUMENT -> deserializerVar + ".ReadDocument(" + schemaVar + ")";
      case ENUM, STRUCTURE, UNION, LIST, SET, MAP ->
          CSharpSymbolProvider.qualified(context.symbolProvider().toSymbol(target))
              + ".Deserialize("
              + deserializerVar
              + ")";
      case INT_ENUM ->
          "("
              + CSharpSymbolProvider.qualified(context.symbolProvider().toSymbol(target))
              + ")"
              + deserializerVar
              + ".ReadInteger("
              + schemaVar
              + ")";
      default ->
          throw new IllegalArgumentException(
              "Unsupported body projection member shape: " + target.getId());
    };
  }

  // =====================================================================
  // gRPC client
  // =====================================================================

  private void writeGrpcClient(
      SymbolProvider sp, Model model, List<OperationShape> operations, String interfaceName) {
    String svcName = CSharpNaming.typeName(service.getId().getName());
    String grpcNs = grpcNamespace();
    String rawClientType = "global::" + grpcNs + "." + svcName + "." + svcName + "Client";
    String clientTypeName = svcName + "GrpcClient";

    writer.write("public sealed class $L : $L", clientTypeName, interfaceName);
    writer.openBlock(
        "{",
        "}",
        () -> {
          writer.write("private readonly $L client;", rawClientType);
          writer.write("");
          writer.write("public $L(ChannelBase channel)", clientTypeName);
          writer.write(
              "    : this(new $L(channel ?? throw new"
                  + " System.ArgumentNullException(nameof(channel)))) { }",
              rawClientType);
          writer.write("");
          writer.write("public $L(CallInvoker callInvoker)", clientTypeName);
          writer.write(
              "    : this(new $L(callInvoker ?? throw new"
                  + " System.ArgumentNullException(nameof(callInvoker)))) { }",
              rawClientType);
          writer.write("");
          writer.write("public $L($L client)", clientTypeName, rawClientType);
          writer.openBlock(
              "{",
              "}",
              () ->
                  writer.write(
                      "this.client = client ?? throw new"
                          + " System.ArgumentNullException(nameof(client));"));
          writer.write("");
          for (OperationShape op : operations) {
            writeGrpcOperationMethod(sp, model, op);
            writer.write("");
          }
        });
  }

  private void writeGrpcOperationMethod(SymbolProvider sp, Model model, OperationShape op) {
    writeGrpcClientUnaryMethod(sp, model, op);
  }

  private void writeGrpcClientUnaryMethod(SymbolProvider sp, Model model, OperationShape op) {
    String operationName = CSharpNaming.typeName(op.getId().getName());
    boolean hasInput = !ShapeSupport.isUnit(op.getInputShape());
    boolean hasOutput = !ShapeSupport.isUnit(op.getOutputShape());
    String grpcInputType = grpcMessageType(op.getInputShape());
    String grpcInputExpr =
        hasInput
            ? GrpcConversions.smithyToGrpc(
                sp, model, model.expectShape(op.getInputShape()), "input", grpcNamespace())
            : "new Google.Protobuf.WellKnownTypes.Empty()";

    writer.write("public async $L", operationSignature(sp, op));
    writer.openBlock(
        "{",
        "}",
        () -> {
          if (hasInput) {
            writer.write("System.ArgumentNullException.ThrowIfNull(input);");
            writer.write("");
          }
          writer.write("$L request = $L;", grpcInputType, grpcInputExpr);
          if (hasOutput) {
            writer.write(
                "var response = await client.$LAsync(request,"
                    + " cancellationToken: cancellationToken).ResponseAsync.ConfigureAwait(false);",
                operationName);
            writer.write(
                "return $L;",
                GrpcConversions.grpcToSmithy(
                    sp,
                    model,
                    model.expectShape(op.getOutputShape()),
                    "response",
                    grpcNamespace()));
          } else {
            writer.write(
                "await client.$LAsync(request,"
                    + " cancellationToken: cancellationToken).ResponseAsync.ConfigureAwait(false);",
                operationName);
          }
        });
  }

  private String grpcMessageType(ShapeId id) {
    if (ShapeSupport.isUnit(id)) return "Google.Protobuf.WellKnownTypes.Empty";
    return "global::" + grpcNamespace() + "." + CSharpNaming.typeName(id.getName());
  }

  private String grpcNamespace() {
    return context.settings().csharpNamespace(service.getId().getNamespace()) + ".Grpc";
  }

  // =====================================================================
  // shared helpers
  // =====================================================================

  public static String bodyProjectionName(StructureShape shape) {
    return CSharpNaming.typeName(shape.getId().getName()) + "HttpBody";
  }

  public static boolean hasResponseBindings(StructureShape output) {
    return output.members().stream()
        .anyMatch(
            m ->
                ShapeSupport.isHttpHeader(m)
                    || ShapeSupport.isHttpPrefixHeaders(m)
                    || ShapeSupport.isHttpPayload(m)
                    || ShapeSupport.isHttpResponseCode(m));
  }

  public static List<MemberShape> responseBodyMembers(StructureShape output) {
    return ShapeSupport.constructorMembers(output).stream()
        .filter(
            m ->
                !ShapeSupport.isHttpHeader(m)
                    && !ShapeSupport.isHttpPrefixHeaders(m)
                    && !ShapeSupport.isHttpResponseCode(m)
                    && !ShapeSupport.isHttpPayload(m))
        .collect(Collectors.toList());
  }

  private static String trimTrailingSlash(String uri) {
    return uri.length() > 1 && uri.endsWith("/") ? uri.substring(0, uri.length() - 1) : uri;
  }

  private String operationSignature(SymbolProvider sp, OperationShape op) {
    boolean hasInput = !ShapeSupport.isUnit(op.getInputShape());
    boolean hasOutput = !ShapeSupport.isUnit(op.getOutputShape());
    String name = CSharpNaming.typeName(op.getId().getName()) + "Async";
    String inputType =
        hasInput
            ? CSharpSymbolProvider.qualified(
                sp.toSymbol(context.model().expectShape(op.getInputShape())))
            : null;
    String outputType =
        hasOutput
            ? CSharpSymbolProvider.qualified(
                sp.toSymbol(context.model().expectShape(op.getOutputShape())))
            : null;
    String returnType =
        hasOutput
            ? "System.Threading.Tasks.Task<" + outputType + ">"
            : "System.Threading.Tasks.Task";
    String params = hasInput ? inputType + " input, " : "";
    return returnType
        + " "
        + name
        + "("
        + params
        + "System.Threading.CancellationToken cancellationToken = default)";
  }
}
