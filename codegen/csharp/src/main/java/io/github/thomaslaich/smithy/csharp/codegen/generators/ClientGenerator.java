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
package io.github.thomaslaich.smithy.csharp.codegen.generators;

import io.github.thomaslaich.smithy.csharp.codegen.CSharpNaming;
import io.github.thomaslaich.smithy.csharp.codegen.CSharpSymbolProvider;
import io.github.thomaslaich.smithy.csharp.codegen.GenerationContext;
import io.github.thomaslaich.smithy.csharp.codegen.RuntimeTypes;
import io.github.thomaslaich.smithy.csharp.codegen.support.AttributeEmitter;
import io.github.thomaslaich.smithy.csharp.codegen.support.ProtocolSupport;
import io.github.thomaslaich.smithy.csharp.codegen.support.ProtocolSupport.Kind;
import io.github.thomaslaich.smithy.csharp.codegen.support.ShapeSupport;
import io.github.thomaslaich.smithy.csharp.codegen.writer.CSharpWriter;
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
import software.amazon.smithy.model.shapes.MemberShape;
import software.amazon.smithy.model.shapes.OperationShape;
import software.amazon.smithy.model.shapes.ServiceShape;
import software.amazon.smithy.model.shapes.ShapeId;
import software.amazon.smithy.model.shapes.StructureShape;
import software.amazon.smithy.model.traits.HttpHeaderTrait;
import software.amazon.smithy.model.traits.HttpPrefixHeadersTrait;
import software.amazon.smithy.model.traits.HttpQueryTrait;
import software.amazon.smithy.model.traits.HttpTrait;
import software.amazon.smithy.utils.SmithyInternalApi;

@SmithyInternalApi
public final class ClientGenerator implements Runnable {

  private static final ShapeId UNIT = ShapeId.from("smithy.api#Unit");

  private final GenerationContext context;
  private final CSharpWriter writer;
  private final ServiceShape service;
  private final boolean emitsHttp;
  private final boolean emitsGrpc;
  private final Kind kind;
  private final String runtime; // ProtocolSupport.runtimeProtocolType(kind)

  public ClientGenerator(GenerationContext c, CSharpWriter w, ServiceShape s) {
    this.context = c;
    this.writer = w;
    this.service = s;
    this.emitsHttp = ProtocolSupport.emitsHttpClient(s);
    this.emitsGrpc = ProtocolSupport.isGrpcService(s);
    this.kind = emitsHttp ? ProtocolSupport.kindOf(s) : Kind.REST_JSON;
    this.runtime = ProtocolSupport.runtimeProtocolType(this.kind);
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
    writer.openBlock(
        "public interface $L {",
        "}",
        interfaceName,
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
      writer.addImport(RuntimeTypes.NSMITHY_CODECS);
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
    writer.openBlock(
        "public sealed class $L : $L {",
        "}",
        typeName,
        interfaceName,
        () -> {
          writer.write(
              "private static readonly ISmithyPayloadCodec DocumentCodec = $L;",
              ProtocolSupport.codecExpression(kind));
          writer.write("private readonly SmithyOperationInvoker invoker;");
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
                  + " options.Middleware))");
          writer.write("{ }");
          writer.write("");
          writer.openBlock(
              "public $L(SmithyOperationInvoker invoker) {",
              "}",
              typeName,
              () ->
                  writer.write(
                      "this.invoker = invoker ?? throw new"
                          + " System.ArgumentNullException(nameof(invoker));"));
          writer.write("");

          for (OperationShape op : operations) writeOperationMethod(sp, model, op);
          for (OperationShape op : operations) writeErrorDeserializer(sp, model, op);

          writeBodyProjectionTypes(sp, model, operations);
        });
  }

  // ---------------- per-operation method ----------------

  private void writeOperationMethod(SymbolProvider sp, Model model, OperationShape op) {
    StructureShape input =
        isUnit(op.getInputShape())
            ? null
            : model.expectShape(op.getInputShape(), StructureShape.class);
    StructureShape output =
        isUnit(op.getOutputShape())
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

    writer.openBlock(
        "public async $L {",
        "}",
        operationSignature(sp, op),
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
            writer.write("request.Headers[\"Accept\"] = [DocumentCodec.MediaType];");
          } else if (input != null && !useDoc) {
            writeRequestHeaders(input);
          }

          // body
          if (rpc || useDoc) {
            if (input != null) {
              writer.write("request.Content = DocumentCodec.Serialize(input);");
              writer.write("request.ContentType = DocumentCodec.MediaType;");
            }
          } else {
            Optional<MemberShape> payload =
                input == null
                    ? Optional.empty()
                    : input.members().stream().filter(ShapeSupport::isHttpPayload).findFirst();
            if (payload.isPresent()) {
              String prop = CSharpNaming.propertyName(payload.get().getMemberName());
              writer.write("request.Content = DocumentCodec.Serialize(input.$L);", prop);
              writer.write("request.ContentType = DocumentCodec.MediaType;");
            } else if (input != null && hasHttpBody(input)) {
              writeRequestBody(input);
            }
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
            "requestUriBuilder.Replace($L, $L.EscapeGreedyLabel($L));",
            CSharpNaming.formatString("{" + m.getMemberName() + "+}"),
            runtime,
            varName);
        writer.write(
            "requestUriBuilder.Replace($L,"
                + " System.Uri.EscapeDataString($L.FormatHttpValue($L)));",
            CSharpNaming.formatString("{" + m.getMemberName() + "}"),
            runtime,
            varName);
      }
      for (MemberShape m : ShapeSupport.sortedMembers(input)) {
        if (!ShapeSupport.isHttpQuery(m)) continue;
        String qn = m.expectTrait(HttpQueryTrait.class).getValue();
        writer.write(
            "$L.AppendQuery(requestUriBuilder, $L, input.$L);",
            runtime,
            CSharpNaming.formatString(qn),
            CSharpNaming.propertyName(m.getMemberName()));
      }
      for (MemberShape m : ShapeSupport.sortedMembers(input)) {
        if (!ShapeSupport.isHttpQueryParams(m)) continue;
        writer.write(
            "$L.AppendQueryMap(requestUriBuilder, input.$L);",
            runtime,
            CSharpNaming.propertyName(m.getMemberName()));
      }
    }
    writer.write("var requestUri = requestUriBuilder.ToString();");
  }

  private void writeRequestHeaders(StructureShape input) {
    for (MemberShape m : ShapeSupport.sortedMembers(input)) {
      if (ShapeSupport.isHttpHeader(m)) {
        String name = m.expectTrait(HttpHeaderTrait.class).getValue();
        writer.write(
            "$L.AddHeader(request.Headers, $L, input.$L);",
            runtime,
            CSharpNaming.formatString(name),
            CSharpNaming.propertyName(m.getMemberName()));
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
        ShapeSupport.sortedMembers(input).stream()
            .filter(ShapeSupport::isHttpBody)
            .collect(Collectors.toList());
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
      writer.write(
          "return $L.DeserializeRequiredBody<$L>(DocumentCodec, response.Content);",
          runtime,
          outputType);
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
              + ")"
          : runtime
              + ".GetHeader<"
              + memberType
              + ">(response.Headers, "
              + CSharpNaming.formatString(name)
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
      return required
          ? runtime
              + ".DeserializeRequiredBody<"
              + memberType
              + ">(DocumentCodec, response.Content)"
          : runtime + ".DeserializeBody<" + memberType + ">(DocumentCodec, response.Content)";
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
    List<ShapeId> errorIds = new ArrayList<>(op.getErrors());
    errorIds.sort(Comparator.comparing(ShapeId::toString));

    writer.openBlock(
        "private static System.Threading.Tasks.ValueTask<System.Exception?> $L(SmithyHttpResponse"
            + " response, System.Threading.CancellationToken cancellationToken) {",
        "}",
        methodName,
        () -> {
          writer.openBlock(
              "if (response.Content.Length == 0) {",
              "}",
              () ->
                  writer.write(
                      "return"
                          + " System.Threading.Tasks.ValueTask.FromResult<System.Exception?>(null);"));
          if (errorIds.isEmpty()) {
            writer.write("");
            writer.write(
                "return System.Threading.Tasks.ValueTask.FromResult<System.Exception?>(null);");
            return;
          }
          switch (kind) {
            case RPC_V2_CBOR -> {
              writer.openBlock(
                  "if (!$L.HasResponse(response)) {",
                  "}",
                  runtime,
                  () ->
                      writer.write(
                          "return"
                              + " System.Threading.Tasks.ValueTask.FromResult<System.Exception?>(null);"));
              writer.write("");
              writer.write("var errorType = $L.DeserializeErrorType(response.Content);", runtime);
              for (ShapeId errId : errorIds) {
                StructureShape err = model.expectShape(errId, StructureShape.class);
                writer.write("");
                writer.openBlock(
                    "if (string.Equals(errorType, $L, System.StringComparison.Ordinal)"
                        + " || string.Equals(errorType, $L, System.StringComparison.Ordinal)) {",
                    "}",
                    CSharpNaming.formatString(errId.getName()),
                    CSharpNaming.formatString(errId.toString()),
                    () -> writeErrorReturn(sp, err));
              }
            }
            case REST_XML -> {
              writer.write("var errorType = $L.DeserializeErrorCode(response.Content);", runtime);
              for (ShapeId errId : errorIds) {
                StructureShape err = model.expectShape(errId, StructureShape.class);
                writer.write("");
                writer.openBlock(
                    "if (string.Equals(errorType, $L, System.StringComparison.Ordinal)) {",
                    "}",
                    CSharpNaming.formatString(errId.getName()),
                    () -> writeErrorReturn(sp, err));
              }
            }
            case REST_JSON -> {
              for (ShapeId errId : errorIds) {
                StructureShape err = model.expectShape(errId, StructureShape.class);
                Integer status = httpErrorCode(err);
                if (status == null) continue;
                writer.write("");
                writer.openBlock(
                    "if ((int)response.StatusCode == $L) {",
                    "}",
                    status,
                    () -> writeErrorReturn(sp, err));
              }
            }
          }
          // fallback: first error. Wrap in an explicit block so the inner `var errorBody`
          // doesn't collide with the per-status branches above (CS0136).
          ShapeId fallback = errorIds.get(0);
          StructureShape err = model.expectShape(fallback, StructureShape.class);
          writer.write("");
          writer.openBlock("{", "}", () -> writeErrorReturn(sp, err));
        });
    writer.write("");
  }

  private String errorConstruction(SymbolProvider sp, StructureShape err, String bodyVar) {
    if (ProtocolSupport.useDocumentBindings(kind)) {
      // Whole error body is a single document.
      String t = CSharpSymbolProvider.qualified(sp.toSymbol(err));
      return runtime + ".DeserializeRequiredBody<" + t + ">(DocumentCodec, response.Content)";
    }
    // Mirror ErrorGenerator's ctor signature: leading `string? message` (always present, even
    // when the shape has no `message` member — that's how System.Exception.Message is wired)
    // followed by the remaining members in constructor order (required first, then optional,
    // each alphabetical) — NOT sortedMembers order, which would mis-align args with parameters.
    Optional<MemberShape> mm = ShapeSupport.errorMessageMember(err);
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
    return err.getTrait(software.amazon.smithy.model.traits.HttpErrorTrait.class)
        .map(t -> t.getCode())
        .orElse(null);
  }

  // ---------------- body projection types ----------------

  private void writeBodyProjectionTypes(SymbolProvider sp, Model model, List<OperationShape> ops) {
    if (ProtocolSupport.useDocumentBindings(kind)) {
      // Whole shape is the body; no projection types needed.
      return;
    }
    Set<ShapeId> emitted = new HashSet<>();
    for (OperationShape op : ops) {
      if (!isUnit(op.getInputShape()) && emitted.add(op.getInputShape())) {
        StructureShape input = model.expectShape(op.getInputShape(), StructureShape.class);
        List<MemberShape> bodyMembers =
            ShapeSupport.constructorMembers(input).stream()
                .filter(ShapeSupport::isHttpBody)
                .collect(Collectors.toList());
        if (!bodyMembers.isEmpty()) writeBodyProjectionType(sp, input, bodyMembers);
      }
      if (!isUnit(op.getOutputShape()) && emitted.add(op.getOutputShape())) {
        StructureShape output = model.expectShape(op.getOutputShape(), StructureShape.class);
        if (hasResponseBindings(output)) {
          List<MemberShape> bodyMembers = responseBodyMembers(output);
          if (!bodyMembers.isEmpty()) writeBodyProjectionType(sp, output, bodyMembers);
        }
      }
    }
    for (OperationShape op : ops) {
      for (ShapeId errId : op.getErrors()) {
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
    AttributeEmitter.writeShapeAttributes(writer, shape);
    writer.openBlock(
        "private sealed class $L {",
        "}",
        typeName,
        () -> {
          writer.write("");
          writer.openBlock(
              "public $L(",
              ") {",
              typeName,
              () -> {
                for (int i = 0; i < bodyMembers.size(); i++) {
                  MemberShape m = bodyMembers.get(i);
                  String type = ShapeSupport.memberTypeExpr(sp, m, ShapeSupport.isNullable(m));
                  writer.write(
                      "$L $L$L",
                      type,
                      CSharpNaming.parameterName(m.getMemberName()),
                      i == bodyMembers.size() - 1 ? "" : ",");
                }
              });
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
            String type = ShapeSupport.memberTypeExpr(sp, m, ShapeSupport.isNullable(m));
            AttributeEmitter.writeMemberAttributes(
                writer, m, ShapeSupport.isSparseTarget(context.model(), m));
            writer.write(
                "public $L $L { get; }", type, CSharpNaming.propertyName(m.getMemberName()));
            writer.write("");
          }
        });
    writer.write("");
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

    writer.openBlock(
        "public sealed class $L : $L {",
        "}",
        clientTypeName,
        interfaceName,
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
          writer.openBlock(
              "public $L($L client) {",
              "}",
              clientTypeName,
              rawClientType,
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
    String operationName = CSharpNaming.typeName(op.getId().getName());
    boolean hasInput = !isUnit(op.getInputShape());
    boolean hasOutput = !isUnit(op.getOutputShape());
    String grpcInputType = grpcMessageType(op.getInputShape());
    String grpcInputExpr =
        hasInput
            ? GrpcConversions.smithyToGrpc(
                sp, model.expectShape(op.getInputShape()), "input", grpcNamespace())
            : "new Google.Protobuf.WellKnownTypes.Empty()";

    writer.openBlock(
        "public async $L {",
        "}",
        operationSignature(sp, op),
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
                    sp, model.expectShape(op.getOutputShape()), "response"));
          } else {
            writer.write(
                "await client.$LAsync(request,"
                    + " cancellationToken: cancellationToken).ResponseAsync.ConfigureAwait(false);",
                operationName);
          }
        });
  }

  private String grpcMessageType(ShapeId id) {
    if (isUnit(id)) return "Google.Protobuf.WellKnownTypes.Empty";
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

  private static boolean isUnit(ShapeId id) {
    return UNIT.equals(id);
  }

  private String operationSignature(SymbolProvider sp, OperationShape op) {
    boolean hasInput = !isUnit(op.getInputShape());
    boolean hasOutput = !isUnit(op.getOutputShape());
    String name = CSharpNaming.typeName(op.getId().getName()) + "Async";
    String returnType =
        hasOutput
            ? "System.Threading.Tasks.Task<"
                + CSharpSymbolProvider.qualified(
                    sp.toSymbol(context.model().expectShape(op.getOutputShape())))
                + ">"
            : "System.Threading.Tasks.Task";
    String params =
        hasInput
            ? CSharpSymbolProvider.qualified(
                    sp.toSymbol(context.model().expectShape(op.getInputShape())))
                + " input, "
            : "";
    return returnType
        + " "
        + name
        + "("
        + params
        + "System.Threading.CancellationToken cancellationToken = default)";
  }
}
