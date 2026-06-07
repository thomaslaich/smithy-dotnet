/*
 * Renders the C# client(s) for a service.
 *
 * For services with one of the supported HTTP protocols
 * (alloy#simpleRestJson, aws.protocols#restJson1, aws.protocols#restXml,
 * smithy.protocols#rpcv2Cbor) emits an `I{Service}Client` interface and a
 * concrete `{Service}Client` class using SmithyOperationInvoker + the matching
 * protocol runtime helper class (RestJsonProtocol / RestXmlProtocol /
 * RpcV2CborProtocol).
 *
 * All HTTP request/response/error wiring is delegated to the protocol helper
 * class at runtime via the operation's functional schema; the generated client
 * only threads inputs/outputs through SerializeRequest / DeserializeResponse /
 * DeserializeError. Per-protocol differences (codec, runtime helper namespace,
 * error discrimination, URI scheme) are owned by the runtime, not codegen.
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
import java.util.List;
import java.util.stream.Collectors;
import software.amazon.smithy.codegen.core.SymbolProvider;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.knowledge.TopDownIndex;
import software.amazon.smithy.model.node.Node;
import software.amazon.smithy.model.shapes.MemberShape;
import software.amazon.smithy.model.shapes.OperationShape;
import software.amazon.smithy.model.shapes.ServiceShape;
import software.amazon.smithy.model.shapes.ShapeId;
import software.amazon.smithy.model.shapes.StructureShape;
import software.amazon.smithy.model.traits.IdempotencyTokenTrait;
import software.amazon.smithy.utils.SmithyInternalApi;

@SmithyInternalApi
public final class ClientGenerator implements Runnable {

  private final GenerationContext context;
  private final CSharpWriter writer;
  private final ServiceShape service;
  private final boolean emitsHttp;
  private final boolean emitsGrpc;
  private final Kind kind;

  public ClientGenerator(GenerationContext c, CSharpWriter w, ServiceShape s) {
    this.context = c;
    this.writer = w;
    this.service = s;
    this.emitsHttp = ProtocolSupport.emitsHttpClient(s);
    this.emitsGrpc = ProtocolSupport.isGrpcService(s);
    this.kind = emitsHttp ? ProtocolSupport.kindOf(s) : Kind.REST_JSON;
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
      writer.addImport(RuntimeTypes.NSMITHY_CORE_SERDE);
      writer.addImport(ProtocolSupport.runtimeProtocolNamespace(kind));
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
        });
  }

  // ---------------- per-operation method ----------------

  private void writeOperationMethod(SymbolProvider sp, Model model, OperationShape op) {
    boolean hasInput = !ShapeSupport.isUnit(op.getInputShape());
    boolean hasOutput = !ShapeSupport.isUnit(op.getOutputShape());
    boolean rpc = kind == Kind.RPC_V2_CBOR;
    String opName = CSharpNaming.typeName(op.getId().getName());
    String deserName = "Deserialize" + opName + "ErrorAsync";
    String protocol = ProtocolSupport.protocolType(kind);
    String schema = SchemaGenerator.functionalOperationSchemaAccessor(context, op);
    String inputArg = hasInput ? "input" : "SmithyUnit.Value";

    writer.write("public async $L", operationSignature(sp, op));
    writer.openBlock(
        "{",
        "}",
        () -> {
          if (hasInput) {
            writer.write("System.ArgumentNullException.ThrowIfNull(input);");
            writeFunctionalIdempotencyTokenDefaults(
                model.expectShape(op.getInputShape(), StructureShape.class));
          }

          if (rpc) {
            // rpcv2Cbor uses a synthetic URI; there are no @http bindings to derive it from.
            String uri =
                "/service/" + service.getId().getName() + "/operation/" + op.getId().getName();
            writer.write(
                "var request = $L.SerializeRequest($L, $L, $L);",
                protocol,
                schema,
                inputArg,
                CSharpNaming.formatString(uri));
          } else {
            writer.write("var request = $L.SerializeRequest($L, $L);", protocol, schema, inputArg);
          }

          if (op.findTrait(TraitIds.REQUEST_COMPRESSION).isPresent()) {
            writer.write(
                "$L.ApplyRequestCompression(request, $L);",
                protocol,
                requestCompressionEncoding(op));
          }
          if (op.findTrait(TraitIds.HTTP_CHECKSUM_REQUIRED).isPresent()) {
            writer.write("$L.ApplyContentMd5(request);", protocol);
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
            writer.write("return $L.DeserializeResponse($L, response);", protocol, schema);
          } else {
            writer.write("return;");
          }
        });
    writer.write("");
  }

  private void writeFunctionalIdempotencyTokenDefaults(StructureShape input) {
    List<MemberShape> idempotencyMembers =
        ShapeSupport.sortedMembers(input).stream()
            .filter(m -> m.hasTrait(IdempotencyTokenTrait.class))
            .filter(ShapeSupport::isNullable)
            .collect(Collectors.toList());
    if (idempotencyMembers.isEmpty()) {
      return;
    }

    writer.write("input = input with");
    writer.openBlock(
        "{",
        "};",
        () -> {
          for (MemberShape member : idempotencyMembers) {
            String prop = CSharpNaming.propertyName(member.getMemberName());
            writer.write("$L = input.$L ?? options.IdempotencyTokenProvider(),", prop, prop);
          }
        });
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

  // ---------------- error deserializer ----------------

  private void writeErrorDeserializer(SymbolProvider sp, Model model, OperationShape op) {
    String opName = CSharpNaming.typeName(op.getId().getName());
    String methodName = "Deserialize" + opName + "ErrorAsync";
    String protocol = ProtocolSupport.protocolType(kind);
    boolean rpc = kind == Kind.RPC_V2_CBOR;
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
          if (errorIds.isEmpty()) {
            writer.write("");
            writer.write(
                "return System.Threading.Tasks.ValueTask.FromResult<System.Exception?>(null);");
            return;
          }

          if (rpc) {
            // Without the rpcv2Cbor protocol header there is no error envelope to read.
            writer.write("if (!$L.HasResponse(response))", protocol);
            writer.openBlock(
                "{",
                "}",
                () ->
                    writer.write(
                        "return"
                            + " System.Threading.Tasks.ValueTask.FromResult<System.Exception?>(null);"));
            writer.write("");
          }

          writer.write("var errorType = $L.DeserializeErrorType(response);", protocol);
          for (ShapeId errId : errorIds) {
            StructureShape err = model.expectShape(errId, StructureShape.class);
            writer.write("");
            if (rpc) {
              // rpcv2Cbor's __type may be a bare shape name or an absolute shape id.
              writer.write(
                  "if (string.Equals(errorType, $L, System.StringComparison.Ordinal)"
                      + " || string.Equals(errorType, $L, System.StringComparison.Ordinal))",
                  CSharpNaming.formatString(errId.getName()),
                  CSharpNaming.formatString(errId.toString()));
            } else {
              writer.write(
                  "if (string.Equals(errorType, $L, System.StringComparison.Ordinal))",
                  CSharpNaming.formatString(errId.getName()));
            }
            writer.openBlock("{", "}", () -> writeFunctionalErrorReturn(sp, err));
          }

          if (!rpc) {
            // Fall back to HTTP status code when the error type is not discriminable from the body.
            for (ShapeId errId : errorIds) {
              StructureShape err = model.expectShape(errId, StructureShape.class);
              Integer status = httpErrorCode(err);
              if (status == null) continue;
              writer.write("");
              writer.write("if ((int)response.StatusCode == $L)", status);
              writer.openBlock("{", "}", () -> writeFunctionalErrorReturn(sp, err));
            }
          }

          // fallback: first error. Wrap in an explicit block so the inner deserialization doesn't
          // collide with the per-status branches above (CS0136). Guard against empty bodies for
          // REST errors that carry body members: with nothing to deserialize we cannot recognise
          // any error, so return null and let InvokeAsync throw a generic SmithyClientException.
          ShapeId fallback = errorIds.get(0);
          StructureShape err = model.expectShape(fallback, StructureShape.class);
          boolean fallbackHasBody = !rpc && !responseBodyMembers(err).isEmpty();
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
                writeFunctionalErrorReturn(sp, err);
              });
        });
    writer.write("");
  }

  /**
   * Functional error return: deserialize the error structure from the response (HTTP bindings +
   * body for REST, whole CBOR body for rpcv2Cbor) via the protocol's {@code DeserializeError}.
   */
  private void writeFunctionalErrorReturn(SymbolProvider sp, StructureShape err) {
    writer.write(
        "return System.Threading.Tasks.ValueTask.FromResult<System.Exception?>("
            + "$L.DeserializeError($L, response));",
        ProtocolSupport.protocolType(kind),
        SchemaGenerator.functionalShapeSchemaAccessor(context, err));
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
