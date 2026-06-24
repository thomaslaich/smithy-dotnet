/*
 * Renders the C# client for a service.
 *
 * Emits a single `I{Service}Client` interface plus a concrete `{Service}Client`. The wire protocol
 * is chosen at construction via an optional `IProtocol` constructor parameter — defaulting to the
 * service's primary declared protocol — over the same invoker/protocol machinery
 * (SmithyOperationInvoker + IServiceProtocol/IOperationProtocol) for every protocol:
 *
 *   new WeatherClient(endpoint);                                  // default (primary) protocol
 *   new LibraryServiceClient(endpoint, protocol: new GrpcProtocol());
 *
 * A service may declare any combination of protocols (alloy#simpleRestJson, aws.protocols#restJson1,
 * aws.protocols#restXml, smithy.protocols#rpcv2Cbor, and/or @grpc); the same client speaks whichever
 * `IProtocol` it is given. Three constructors give a clean ownership split:
 *   - (endpoint, ...)   — the client creates and owns its HttpClient (HTTP/2 when the protocol
 *                         requires it via `IProtocol.RequiresHttp2`).
 *   - (httpClient, ...) — the caller owns the HttpClient; the endpoint comes from its BaseAddress.
 *                         This is the only HttpClient-taking constructor, so IHttpClientFactory's
 *                         AddHttpClient<I,T> resolves the client unambiguously.
 *   - (invoker, ...)    — the caller owns the whole transport/middleware pipeline (DI, testing).
 *
 * All request/response/error wiring is delegated to the bound protocol at runtime via each
 * operation's functional schema; the generated client only threads inputs/outputs through
 * SerializeRequest / DeserializeResponse / DeserializeError, and applies protocol-agnostic request
 * mutations (compression, content-MD5) via SmithyRequestModifiers.
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
import software.amazon.smithy.model.knowledge.ServiceIndex;
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

  public ClientGenerator(GenerationContext c, CSharpWriter w, ServiceShape s) {
    this.context = c;
    this.writer = w;
    this.service = s;
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

    // Interface — IDisposable so a client that owns its HttpClient is released via `using` or DI.
    writer.write("public interface $L : System.IDisposable", interfaceName);
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

    // Services with no supported protocol get the interface only; there is nothing to wire.
    List<Kind> kinds = ProtocolSupport.declaredKinds(service);
    if (kinds.isEmpty()) {
      return;
    }

    writer.addImport(RuntimeTypes.NSMITHY_CLIENT);
    writer.addImport(RuntimeTypes.NSMITHY_HTTP);
    writer.addImport(RuntimeTypes.NSMITHY_CORE_SERDE);
    // The generated client only names the primary protocol (the default constructor argument);
    // callers selecting another declared protocol reference its namespace from their own code.
    writer.addImport(ProtocolSupport.runtimeProtocolNamespace(kinds.get(0)));

    writeClient(sp, model, operations, typeName, interfaceName, kinds);
  }

  // =====================================================================
  // client
  // =====================================================================

  private void writeClient(
      SymbolProvider sp,
      Model model,
      List<OperationShape> operations,
      String typeName,
      String interfaceName,
      List<Kind> kinds) {
    String primaryProtocol = ProtocolSupport.protocolType(kinds.get(0));
    String serviceSchema = SchemaGenerator.serviceSchemaAccessor(context, service);
    // The idempotency-token provider is only stored/used when an operation has a nullable
    // @idempotencyToken member; emitting the field unconditionally would be an unused private
    // field.
    boolean needsIdempotency =
        operations.stream().anyMatch(op -> operationNeedsIdempotencyToken(model, op));
    writer.write("public sealed class $L : $L", typeName, interfaceName);
    writer.openBlock(
        "{",
        "}",
        () -> {
          writer.write("private readonly SmithyOperationInvoker invoker;");
          // Only set when the client created the HttpClient itself (the endpoint ctor); null when
          // the caller supplied an HttpClient or invoker, so Dispose never touches what it doesn't
          // own.
          writer.write("private readonly System.Net.Http.HttpClient? ownedHttpClient;");
          if (needsIdempotency) {
            writer.write("private readonly System.Func<string> idempotencyTokenProvider;");
          }
          // The protocol is bound at construction; per-operation protocols are built once from it.
          for (OperationShape op : operations) {
            writer.write(
                "private readonly IOperationProtocol<$L, $L> $LProtocol;",
                SchemaGenerator.operationShapeType(context, op.getInputShape()),
                SchemaGenerator.operationShapeType(context, op.getOutputShape()),
                CSharpNaming.typeName(op.getId().getName()));
          }
          writer.write("");

          // Constructor: endpoint convenience overload. The endpoint argument wins over any
          // Endpoint already present on config; construction then flows through the config
          // constructor so there is only one implementation path.
          writer.write(
              "public $L(System.Uri endpoint, $LConfig? config = null) :"
                  + " this(WithEndpoint(endpoint, config))",
              typeName,
              typeName);
          writer.openBlock("{", "}", () -> {});
          writer.write("");

          // Canonical config implementation for the endpoint constructor. This stays private so
          // the public surface has one direct-construction path (`endpoint, config?`) while config
          // remains the internal model.
          writer.write("private $L($LConfig config)", typeName, typeName);
          writer.openBlock(
              "{",
              "}",
              () -> {
                writer.write("System.ArgumentNullException.ThrowIfNull(config);");
                writer.write(
                    "var endpoint = config.Endpoint ?? throw new System.ArgumentException(");
                writer.write(
                    "    \"Config.Endpoint must be set; otherwise use the constructor that takes an"
                        + " HttpClient.\", nameof(config));");
                writer.write(
                    "var resolvedProtocol = config.Protocol ?? new $L();", primaryProtocol);
                writer.write("var httpClient = CreateDefaultHttpClient(resolvedProtocol);");
                writer.write("this.ownedHttpClient = httpClient;");
                writer.write(
                    "this.invoker = new SmithyOperationInvoker(new"
                        + " HttpClientTransport(httpClient, endpoint),"
                        + " SmithyAuthSchemeResolver.Resolve(endpoint, $L, ModeledAuthSchemes,"
                        + " config.AuthSchemes, config.Middleware));",
                    serviceSchema);
                writeIdempotencyAssignment(needsIdempotency);
                writer.write(
                    "var serviceProtocol = resolvedProtocol.ForService($L);", serviceSchema);
                writeOperationBindings(operations);
              });
          writer.write("");

          // Constructor: bring your own HttpClient (e.g. from IHttpClientFactory /
          // AddHttpClient<I,T>). The endpoint comes from Config.Endpoint, falling back to the
          // HttpClient's BaseAddress.
          writer.write(
              "public $L(System.Net.Http.HttpClient httpClient, $LConfig? config = null)",
              typeName,
              typeName);
          writer.openBlock(
              "{",
              "}",
              () -> {
                writer.write("System.ArgumentNullException.ThrowIfNull(httpClient);");
                writer.write("config ??= new $LConfig();", typeName);
                writer.write(
                    "var endpoint = config.Endpoint ?? httpClient.BaseAddress ?? throw new"
                        + " System.ArgumentException(");
                writer.write(
                    "    \"Set Config.Endpoint or httpClient.BaseAddress.\", nameof(httpClient));");
                writer.write(
                    "var resolvedProtocol = config.Protocol ?? new $L();", primaryProtocol);
                writer.write(
                    "this.invoker = new SmithyOperationInvoker(new"
                        + " HttpClientTransport(httpClient, endpoint),"
                        + " SmithyAuthSchemeResolver.Resolve(endpoint, $L, ModeledAuthSchemes,"
                        + " config.AuthSchemes, config.Middleware));",
                    serviceSchema);
                writeIdempotencyAssignment(needsIdempotency);
                writer.write(
                    "var serviceProtocol = resolvedProtocol.ForService($L);", serviceSchema);
                writeOperationBindings(operations);
              });
          writer.write("");

          // Constructor: bring your own invoker (custom transport/middleware, DI, testing). The
          // invoker already owns the pipeline, so config.AuthSchemes/Middleware do not apply here;
          // only Protocol and IdempotencyTokenProvider are read.
          writer.write(
              "public $L(SmithyOperationInvoker invoker, $LConfig? config = null)",
              typeName,
              typeName);
          writer.openBlock(
              "{",
              "}",
              () -> {
                writer.write(
                    "this.invoker = invoker ?? throw new"
                        + " System.ArgumentNullException(nameof(invoker));");
                writer.write("config ??= new $LConfig();", typeName);
                writer.write(
                    "var resolvedProtocol = config.Protocol ?? new $L();", primaryProtocol);
                writeIdempotencyAssignment(needsIdempotency);
                writer.write(
                    "var serviceProtocol = resolvedProtocol.ForService($L);", serviceSchema);
                writeOperationBindings(operations);
              });
          writer.write("");

          writer.write(
              "private static $LConfig WithEndpoint(System.Uri endpoint, $LConfig? config)",
              typeName,
              typeName);
          writer.openBlock(
              "{",
              "}",
              () -> {
                writer.write("System.ArgumentNullException.ThrowIfNull(endpoint);");
                writer.write("config ??= new $LConfig();", typeName);
                writer.write("config.Endpoint = endpoint;");
                writer.write("return config;");
              });
          writer.write("");

          // The auth schemes the service models, in Smithy's effective priority order (the @auth
          // trait, or all of the service's auth traits in alphabetical order by shape id). The
          // resolver installs the first of these for which the caller configured a matching scheme.
          writer.write(
              "private static readonly System.Collections.Generic.IReadOnlyList<string>"
                  + " ModeledAuthSchemes = $L;",
              modeledAuthSchemesLiteral());
          writer.write("");

          writer.write(
              "private static System.Net.Http.HttpClient CreateDefaultHttpClient(IProtocol"
                  + " protocol) =>");
          writer.write("    protocol.RequiresHttp2");
          writer.write("        ? new System.Net.Http.HttpClient");
          writer.write("        {");
          writer.write("            DefaultRequestVersion = System.Net.HttpVersion.Version20,");
          writer.write(
              "            DefaultVersionPolicy ="
                  + " System.Net.Http.HttpVersionPolicy.RequestVersionExact,");
          writer.write("        }");
          writer.write("        : new System.Net.Http.HttpClient();");
          if (needsIdempotency) {
            writer.write("");
            writer.write(
                "private static string DefaultIdempotencyToken() =>"
                    + " System.Guid.NewGuid().ToString();");
          }
          writer.write("");

          // Disposes the HttpClient the client created itself; a no-op when the caller supplied the
          // HttpClient or invoker (ownedHttpClient is null), so injected transports are never
          // closed.
          writer.write("public void Dispose() => ownedHttpClient?.Dispose();");
          writer.write("");

          for (OperationShape op : operations) writeOperationMethod(sp, model, op);
          for (OperationShape op : operations) writeErrorDeserializer(sp, model, op);
        });
    writer.write("");
    writeConfigClass(typeName);
  }

  /**
   * Renders the per-service client config: a sealed subclass of the runtime's {@code
   * SmithyClientConfig}. It inherits the common knobs today; service-specific options (e.g.
   * endpoint client-context params) can be added here later without changing the client's
   * constructor surface.
   */
  private void writeConfigClass(String typeName) {
    writer.write("public sealed class $LConfig : SmithyClientConfig", typeName);
    writer.openBlock("{", "}", () -> {});
  }

  /**
   * Renders the service's effective auth schemes as a C# array literal of shape-id strings, in
   * Smithy priority order. {@link ServiceIndex#getEffectiveAuthSchemes} applies the spec rules: the
   * {@code @auth} trait when present, otherwise every auth trait on the service ordered
   * alphabetically by absolute shape id. An empty result renders as an empty array.
   */
  private String modeledAuthSchemesLiteral() {
    List<String> ids =
        ServiceIndex.of(context.model()).getEffectiveAuthSchemes(service).keySet().stream()
            .map(ShapeId::toString)
            .collect(Collectors.toList());
    if (ids.isEmpty()) {
      return "System.Array.Empty<string>()";
    }
    return "new string[] { "
        + ids.stream().map(CSharpNaming::formatString).collect(Collectors.joining(", "))
        + " }";
  }

  /**
   * In a constructor body, binds the idempotency-token provider field when the service needs it.
   */
  private void writeIdempotencyAssignment(boolean needsIdempotency) {
    if (needsIdempotency) {
      writer.write(
          "this.idempotencyTokenProvider = config.IdempotencyTokenProvider ??"
              + " DefaultIdempotencyToken;");
    }
  }

  private boolean operationNeedsIdempotencyToken(Model model, OperationShape op) {
    if (ShapeSupport.isUnit(op.getInputShape())) {
      return false;
    }
    StructureShape input = model.expectShape(op.getInputShape(), StructureShape.class);
    return ShapeSupport.sortedMembers(input).stream()
        .anyMatch(m -> m.hasTrait(IdempotencyTokenTrait.class) && ShapeSupport.isNullable(m));
  }

  // ---------------- operation bindings ----------------

  /**
   * Binds each operation's protocol from the local {@code serviceProtocol} in a constructor body.
   */
  private void writeOperationBindings(List<OperationShape> operations) {
    for (OperationShape op : operations) {
      writer.write(
          "this.$LProtocol = serviceProtocol.ForOperation($L);",
          CSharpNaming.typeName(op.getId().getName()),
          SchemaGenerator.operationSchemaAccessor(context, op));
    }
  }

  // ---------------- per-operation method ----------------

  private void writeOperationMethod(SymbolProvider sp, Model model, OperationShape op) {
    boolean hasInput = !ShapeSupport.isUnit(op.getInputShape());
    boolean hasOutput = !ShapeSupport.isUnit(op.getOutputShape());
    String opName = CSharpNaming.typeName(op.getId().getName());
    String deserName = "Deserialize" + opName + "ErrorAsync";
    String inputArg = hasInput ? "input" : "SmithyUnit.Value";

    writer.write("public async $L", operationSignature(sp, op));
    writer.openBlock(
        "{",
        "}",
        () -> {
          if (hasInput) {
            writer.write("System.ArgumentNullException.ThrowIfNull(input);");
            writeIdempotencyTokenDefaults(
                model.expectShape(op.getInputShape(), StructureShape.class));
          }

          // The operation-bound protocol owns request serialization (and, for rpc, the path).
          writer.write("var request = $LProtocol.SerializeRequest($L);", opName, inputArg);

          if (op.findTrait(TraitIds.REQUEST_COMPRESSION).isPresent()) {
            writer.write(
                "SmithyRequestModifiers.ApplyRequestCompression(request, $L);",
                requestCompressionEncoding(op));
          }
          if (op.findTrait(TraitIds.HTTP_CHECKSUM_REQUIRED).isPresent()) {
            writer.write("SmithyRequestModifiers.ApplyContentMd5(request);");
          }

          writer.write("");
          writer.write(
              "var response = await invoker.InvokeAsync($L, $L, request, $L,"
                  + " $LProtocol.IsErrorResponse, cancellationToken).ConfigureAwait(false);",
              CSharpNaming.formatString(service.getId().getName()),
              CSharpNaming.formatString(op.getId().getName()),
              deserName,
              opName);

          if (hasOutput) {
            writer.write("");
            writer.write("return $LProtocol.DeserializeResponse(response);", opName);
          } else {
            writer.write("return;");
          }
        });
    writer.write("");
  }

  private void writeIdempotencyTokenDefaults(StructureShape input) {
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
            writer.write("$L = input.$L ?? this.idempotencyTokenProvider(),", prop, prop);
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
    String receiver = opName + "Protocol";
    List<ShapeId> errorIds = new ArrayList<>(op.getErrors(service));
    errorIds.sort(Comparator.comparing(ShapeId::toString));

    writer.write(
        "private System.Threading.Tasks.ValueTask<System.Exception?> $L(SmithyHttpResponse"
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

          // The bound protocol owns error discrimination; its RequiresErrorDiscriminator /
          // SupportsHttpStatusErrorFallback flags adapt this uniform dispatch to REST vs rpc/gRPC.
          writer.write("var errorType = $L.GetErrorDiscriminator(response);", receiver);
          writer.write("if (errorType is null && $L.RequiresErrorDiscriminator)", receiver);
          writer.openBlock(
              "{",
              "}",
              () ->
                  writer.write(
                      "return"
                          + " System.Threading.Tasks.ValueTask.FromResult<System.Exception?>(null);"));

          writer.write("");
          writer.write("if (errorType is not null)");
          writer.openBlock(
              "{",
              "}",
              () -> {
                for (ShapeId errId : errorIds) {
                  StructureShape err = model.expectShape(errId, StructureShape.class);
                  // The discriminator may be a bare shape name (REST, rpcv2Cbor) or an absolute
                  // shape id (rpcv2Cbor); accept either.
                  writer.write(
                      "if (string.Equals(errorType, $L, System.StringComparison.Ordinal)"
                          + " || string.Equals(errorType, $L, System.StringComparison.Ordinal))",
                      CSharpNaming.formatString(errId.getName()),
                      CSharpNaming.formatString(errId.toString()));
                  writer.openBlock("{", "}", () -> writeErrorReturn(sp, err, receiver));
                }
              });

          // REST-only fallback to the HTTP status code, gated at runtime by the bound protocol.
          List<ShapeId> statusErrors =
              errorIds.stream()
                  .filter(id -> httpErrorCode(model.expectShape(id, StructureShape.class)) != null)
                  .collect(Collectors.toList());
          if (!statusErrors.isEmpty()) {
            writer.write("");
            writer.write("if ($L.SupportsHttpStatusErrorFallback)", receiver);
            writer.openBlock(
                "{",
                "}",
                () -> {
                  for (ShapeId errId : statusErrors) {
                    StructureShape err = model.expectShape(errId, StructureShape.class);
                    writer.write("if ((int)response.StatusCode == $L)", httpErrorCode(err));
                    writer.openBlock("{", "}", () -> writeErrorReturn(sp, err, receiver));
                  }
                });
          }

          // Fallback: first error. For REST errors that carry body members, an empty body means we
          // recognised nothing — return null so InvokeAsync throws a generic SmithyClientException.
          // rpc/gRPC never guard on body emptiness (gated by SupportsHttpStatusErrorFallback).
          ShapeId fallback = errorIds.get(0);
          StructureShape err = model.expectShape(fallback, StructureShape.class);
          boolean fallbackHasBody = !responseBodyMembers(err).isEmpty();
          writer.write("");
          if (fallbackHasBody) {
            writer.write(
                "if ($L.SupportsHttpStatusErrorFallback && response.Content.Length == 0)",
                receiver);
            writer.openBlock(
                "{",
                "}",
                () ->
                    writer.write(
                        "return"
                            + " System.Threading.Tasks.ValueTask.FromResult<System.Exception?>(null);"));
            writer.write("");
          }
          writeErrorReturn(sp, err, receiver);
        });
    writer.write("");
  }

  /**
   * Functional error return: deserialize the error structure from the response (HTTP bindings +
   * body for REST, whole CBOR/proto body for rpc/gRPC) via {@code receiver.DeserializeError(...)},
   * where the receiver is the operation-bound protocol field.
   */
  private void writeErrorReturn(SymbolProvider sp, StructureShape err, String receiver) {
    writer.write(
        "return System.Threading.Tasks.ValueTask.FromResult<System.Exception?>("
            + "$L.DeserializeError($L, response));",
        receiver,
        SchemaGenerator.shapeSchemaAccessor(context, err));
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
