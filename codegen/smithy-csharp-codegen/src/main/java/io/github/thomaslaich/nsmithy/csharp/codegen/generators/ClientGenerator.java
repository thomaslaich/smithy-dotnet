/*
 * Renders the generated C# service client and client interface.
 * Protocol-specific wire behavior lives behind IServiceProtocol and the bound operation protocols;
 * this generator wires those protocols into idiomatic client methods and constructors.
 */
package io.github.thomaslaich.nsmithy.csharp.codegen.generators;

import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpNaming;
import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpSymbolProvider;
import io.github.thomaslaich.nsmithy.csharp.codegen.GenerationContext;
import io.github.thomaslaich.nsmithy.csharp.codegen.RuntimeTypes;
import io.github.thomaslaich.nsmithy.csharp.codegen.support.ProtocolSupport;
import io.github.thomaslaich.nsmithy.csharp.codegen.support.ProtocolSupport.Kind;
import io.github.thomaslaich.nsmithy.csharp.codegen.support.ShapeSupport;
import io.github.thomaslaich.nsmithy.csharp.codegen.writer.CSharpWriter;
import java.util.Comparator;
import java.util.List;
import java.util.Optional;
import java.util.stream.Collectors;
import software.amazon.smithy.codegen.core.SymbolProvider;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.knowledge.PaginatedIndex;
import software.amazon.smithy.model.knowledge.PaginationInfo;
import software.amazon.smithy.model.knowledge.ServiceIndex;
import software.amazon.smithy.model.knowledge.TopDownIndex;
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
            paginationInfo(op)
                .ifPresent(
                    info -> {
                      writer.write("$L;", paginatorPagesSignature(sp, op));
                      paginatorItemsSignature(sp, info)
                          .ifPresent(signature -> writer.write("$L;", signature));
                    });
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
    if (operations.stream().anyMatch(op -> isEventStreamOperation(model, op))
        && supportsEventStreamOperations()) {
      writer.addImport(RuntimeTypes.NSMITHY_PROTOCOLS_GRPC);
    }

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
    boolean hasUnaryOperations =
        operations.stream().anyMatch(op -> !isEventStreamOperation(model, op));
    boolean hasEventStreamOperations =
        operations.stream().anyMatch(op -> isEventStreamOperation(model, op));
    boolean wiresEventStreamOperations =
        hasEventStreamOperations && supportsEventStreamOperations();
    boolean needsHttpClient = hasUnaryOperations || wiresEventStreamOperations;
    boolean needsIdempotency =
        operations.stream().anyMatch(op -> operationCanDefaultIdempotencyToken(model, op));
    writer.write("public sealed class $L : $L", typeName, interfaceName);
    writer.openBlock(
        "{",
        "}",
        () -> {
          if (hasUnaryOperations) {
            writer.write("private readonly SmithyClientRuntime runtime;");
          }
          if (wiresEventStreamOperations) {
            writer.write("private readonly SmithyEventStreamOperationInvoker eventStreamInvoker;");
          }
          // Only set when the client created the HttpClient itself (the endpoint ctor); null when
          // the caller supplied an HttpClient or runtime, so Dispose never touches what it doesn't
          // own.
          if (needsHttpClient) {
            writer.write("private readonly System.Net.Http.HttpClient? ownedHttpClient;");
          }
          if (needsIdempotency) {
            writer.write("private readonly System.Func<string> idempotencyTokenProvider;");
          }
          // The protocol is bound at construction; per-operation protocols are built once from it.
          for (OperationShape op : operations) {
            if (isEventStreamOperation(model, op)) {
              if (wiresEventStreamOperations) {
                writeEventStreamProtocolField(sp, model, op);
              }
            } else {
              writer.write(
                  "private readonly SmithyOperationBinding<$L, $L> $LBinding;",
                  SchemaGenerator.operationShapeType(context, op.getInputShape()),
                  SchemaGenerator.operationShapeType(context, op.getOutputShape()),
                  CSharpNaming.typeName(op.getId().getName()));
            }
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
                if (needsHttpClient) {
                  writer.write(
                      "var endpoint = config.Endpoint ?? throw new System.ArgumentException(");
                  writer.write(
                      "    \"Config.Endpoint must be set; otherwise use the constructor that takes"
                          + " an HttpClient.\", nameof(config));");
                  writer.write(
                      "var resolvedProtocol = config.Protocol ?? new $L();", primaryProtocol);
                  writer.write("var httpClient = CreateDefaultHttpClient(resolvedProtocol);");
                  writer.write("this.ownedHttpClient = httpClient;");
                  writer.write(
                      "var serviceProtocol = resolvedProtocol.ForService($L);", serviceSchema);
                }
                if (hasUnaryOperations) {
                  writer.write(
                      "this.runtime = new SmithyClientRuntime(new"
                          + " HttpClientTransport(httpClient),"
                          + " config.Interceptors,"
                          + " config.RetryStrategy,"
                          + " endpoint,"
                          + " config.OperationTimeout,"
                          + " config.EndpointResolver,"
                          + " SmithyAuthSchemeResolver.ResolveInterceptors(endpoint, $L,"
                          + " ModeledAuthSchemes, config.AuthSchemes));",
                      serviceSchema);
                }
                if (wiresEventStreamOperations) {
                  writer.write(
                      "this.eventStreamInvoker = new SmithyEventStreamOperationInvoker(new"
                          + " GrpcEventStreamHttpClientTransport(httpClient, endpoint));");
                }
                writeIdempotencyAssignment(needsIdempotency);
                if (needsHttpClient) {
                  writeOperationBindings(operations);
                }
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
                if (needsHttpClient) {
                  writer.write("config ??= new $LConfig();", typeName);
                  writer.write(
                      "var endpoint = config.Endpoint ?? httpClient.BaseAddress ?? throw new"
                          + " System.ArgumentException(");
                  writer.write(
                      "    \"Set Config.Endpoint or httpClient.BaseAddress.\","
                          + " nameof(httpClient));");
                  writer.write(
                      "var resolvedProtocol = config.Protocol ?? new $L();", primaryProtocol);
                  writer.write(
                      "var serviceProtocol = resolvedProtocol.ForService($L);", serviceSchema);
                }
                if (hasUnaryOperations) {
                  writer.write(
                      "this.runtime = new SmithyClientRuntime(new"
                          + " HttpClientTransport(httpClient),"
                          + " config.Interceptors,"
                          + " config.RetryStrategy,"
                          + " endpoint,"
                          + " config.OperationTimeout,"
                          + " config.EndpointResolver,"
                          + " SmithyAuthSchemeResolver.ResolveInterceptors(endpoint, $L,"
                          + " ModeledAuthSchemes, config.AuthSchemes));",
                      serviceSchema);
                }
                if (wiresEventStreamOperations) {
                  writer.write(
                      "this.eventStreamInvoker = new SmithyEventStreamOperationInvoker(new"
                          + " GrpcEventStreamHttpClientTransport(httpClient, endpoint));");
                }
                writeIdempotencyAssignment(needsIdempotency);
                if (needsHttpClient) {
                  writeOperationBindings(operations);
                }
              });
          writer.write("");

          if (!wiresEventStreamOperations) {
            // Constructor: bring your own runtime (custom transport/interceptors, DI, testing). The
            // runtime already owns the transport/interceptor pipeline, so config.AuthSchemes and
            // config.Interceptors do not apply here; only Protocol and IdempotencyTokenProvider are
            // read.
            if (hasUnaryOperations) {
              writer.write(
                  "public $L(SmithyClientRuntime runtime, $LConfig? config = null)",
                  typeName,
                  typeName);
              writer.openBlock(
                  "{",
                  "}",
                  () -> {
                    writer.write(
                        "this.runtime = runtime ?? throw new"
                            + " System.ArgumentNullException(nameof(runtime));");
                    writer.write("config ??= new $LConfig();", typeName);
                    writer.write(
                        "var resolvedProtocol = config.Protocol ?? new $L();", primaryProtocol);
                    writeIdempotencyAssignment(needsIdempotency);
                    writer.write(
                        "var serviceProtocol = resolvedProtocol.ForService($L);", serviceSchema);
                    writeOperationBindings(operations);
                  });
              writer.write("");
            }
          } else if (hasUnaryOperations) {
            writer.write(
                "public $L(SmithyClientRuntime runtime, SmithyEventStreamOperationInvoker"
                    + " eventStreamInvoker, $LConfig? config = null)",
                typeName,
                typeName);
            writer.openBlock(
                "{",
                "}",
                () -> {
                  writer.write(
                      "this.runtime = runtime ?? throw new"
                          + " System.ArgumentNullException(nameof(runtime));");
                  writer.write(
                      "this.eventStreamInvoker = eventStreamInvoker ?? throw new"
                          + " System.ArgumentNullException(nameof(eventStreamInvoker));");
                  writer.write("config ??= new $LConfig();", typeName);
                  writer.write(
                      "var resolvedProtocol = config.Protocol ?? new $L();", primaryProtocol);
                  writeIdempotencyAssignment(needsIdempotency);
                  writer.write(
                      "var serviceProtocol = resolvedProtocol.ForService($L);", serviceSchema);
                  writeOperationBindings(operations);
                });
            writer.write("");
          } else {
            writer.write(
                "public $L(SmithyEventStreamOperationInvoker eventStreamInvoker, $LConfig? config ="
                    + " null)",
                typeName,
                typeName);
            writer.openBlock(
                "{",
                "}",
                () -> {
                  writer.write(
                      "this.eventStreamInvoker = eventStreamInvoker ?? throw new"
                          + " System.ArgumentNullException(nameof(eventStreamInvoker));");
                  writer.write("config ??= new $LConfig();", typeName);
                  writer.write(
                      "var resolvedProtocol = config.Protocol ?? new $L();", primaryProtocol);
                  writer.write(
                      "var serviceProtocol = resolvedProtocol.ForService($L);", serviceSchema);
                  writeOperationBindings(operations);
                });
            writer.write("");
          }

          // Copies the caller's config before setting the endpoint, so constructing a client
          // never mutates a config instance the caller may share with other clients.
          writer.write(
              "private static $LConfig WithEndpoint(System.Uri endpoint, $LConfig? config)",
              typeName,
              typeName);
          writer.openBlock(
              "{",
              "}",
              () -> {
                writer.write("System.ArgumentNullException.ThrowIfNull(endpoint);");
                writer.write(
                    "var copy = config is null ? new $LConfig() : new $LConfig(config);",
                    typeName,
                    typeName);
                writer.write("copy.Endpoint = endpoint;");
                writer.write("return copy;");
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
          // HttpClient or runtime (ownedHttpClient is null), so injected transports are never
          // closed.
          if (needsHttpClient) {
            writer.write("public void Dispose() => ownedHttpClient?.Dispose();");
          } else {
            writer.write("public void Dispose() { }");
          }
          writer.write("");

          for (OperationShape op : operations) {
            writeOperationMethod(sp, model, op);
            paginationInfo(op).ifPresent(info -> writePaginatorMethods(sp, op, info));
          }
        });
    writer.write("");
    writeConfigClass(typeName);
  }

  /**
   * Renders the per-service client config: a sealed subclass of the runtime's {@code
   * SmithyClientConfig}. It inherits the common knobs today; service-specific options (e.g.
   * endpoint client-context params) can be added here later without changing the client's
   * constructor surface. The copy constructor backs the client's copy-at-construction semantics.
   */
  private void writeConfigClass(String typeName) {
    writer.write("public sealed class $LConfig : SmithyClientConfig", typeName);
    writer.openBlock(
        "{",
        "}",
        () -> {
          writer.write("public $LConfig() { }", typeName);
          writer.write("");
          writer.write("public $LConfig($LConfig source) : base(source) { }", typeName, typeName);
        });
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

  private boolean operationCanDefaultIdempotencyToken(Model model, OperationShape op) {
    // Client/bidirectional event streams take the input event sequence as the generated method
    // parameter, so there is no modeled input container to rewrite with a default token. Unary,
    // blob-streaming, and server-event-streaming operations keep a normal input container.
    return !isInputStreaming(model, op) && operationNeedsIdempotencyToken(model, op);
  }

  // ---------------- operation bindings ----------------

  /**
   * Binds each operation's protocol from the local {@code serviceProtocol} in a constructor body.
   */
  private void writeOperationBindings(List<OperationShape> operations) {
    for (OperationShape op : operations) {
      if (isEventStreamOperation(context.model(), op)) {
        if (supportsEventStreamOperations()) {
          writeEventStreamOperationBinding(context.model(), op);
        }
        continue;
      }
      String operationSchema = SchemaGenerator.operationSchemaAccessor(context, op);
      writer.write(
          "this.$LBinding = new SmithyOperationBinding<$L, $L>($L.Id, $L.Id,"
              + " serviceProtocol.ForOperation($L), $L);",
          CSharpNaming.typeName(op.getId().getName()),
          SchemaGenerator.operationShapeType(context, op.getInputShape()),
          SchemaGenerator.operationShapeType(context, op.getOutputShape()),
          SchemaGenerator.serviceSchemaAccessor(context, service),
          operationSchema,
          operationSchema,
          operationAuthSchemesLiteral(op));
    }
  }

  /**
   * The operation's effective auth schemes in Smithy priority order: the service's effective
   * schemes, overridden by a per-operation {@code @auth} trait. Rendered as a C# array literal of
   * shape-id strings for the operation binding; the runtime selects the configured interceptor per
   * invocation from this list.
   */
  private String operationAuthSchemesLiteral(OperationShape op) {
    List<String> ids =
        ServiceIndex.of(context.model()).getEffectiveAuthSchemes(service, op).keySet().stream()
            .map(ShapeId::toString)
            .collect(Collectors.toList());
    if (ids.isEmpty()) {
      return "System.Array.Empty<string>()";
    }
    return "new string[] { "
        + ids.stream().map(CSharpNaming::formatString).collect(Collectors.joining(", "))
        + " }";
  }

  private void writeEventStreamProtocolField(SymbolProvider sp, Model model, OperationShape op) {
    String opName = CSharpNaming.typeName(op.getId().getName());
    String inputType = SchemaGenerator.operationShapeType(context, op.getInputShape());
    String outputType = SchemaGenerator.operationShapeType(context, op.getOutputShape());
    if (isInputStreaming(model, op) && isOutputStreaming(model, op)) {
      writer.write(
          "private readonly IBidirectionalEventStreamOperationProtocol<$L, $L> $LProtocol;",
          streamingEventType(sp, model, op.getInputShape()),
          streamingEventType(sp, model, op.getOutputShape()),
          opName);
    } else if (isOutputStreaming(model, op)) {
      writer.write(
          "private readonly IServerEventStreamOperationProtocol<$L, $L> $LProtocol;",
          inputType,
          streamingEventType(sp, model, op.getOutputShape()),
          opName);
    } else {
      writer.write(
          "private readonly IClientEventStreamOperationProtocol<$L, $L> $LProtocol;",
          streamingEventType(sp, model, op.getInputShape()),
          outputType,
          opName);
    }
  }

  private void writeEventStreamOperationBinding(Model model, OperationShape op) {
    String opName = CSharpNaming.typeName(op.getId().getName());
    String operationSchema = SchemaGenerator.operationSchemaAccessor(context, op);
    if (isInputStreaming(model, op) && isOutputStreaming(model, op)) {
      writer.write(
          "this.$LProtocol = serviceProtocol.ForBidirectionalEventStreamOperation($L,"
              + " $L, $L);",
          opName,
          operationSchema,
          streamingEventSchema(model, op.getInputShape()),
          streamingEventSchema(model, op.getOutputShape()));
    } else if (isOutputStreaming(model, op)) {
      writer.write(
          "this.$LProtocol = serviceProtocol.ForServerEventStreamOperation($L, $L);",
          opName,
          operationSchema,
          streamingEventSchema(model, op.getOutputShape()));
    } else {
      writer.write(
          "this.$LProtocol = serviceProtocol.ForClientEventStreamOperation($L, $L);",
          opName,
          operationSchema,
          streamingEventSchema(model, op.getInputShape()));
    }
  }

  // ---------------- per-operation method ----------------

  private void writeOperationMethod(SymbolProvider sp, Model model, OperationShape op) {
    if (isEventStreamOperation(model, op)) {
      writeEventStreamOperationMethod(sp, model, op);
      return;
    }

    boolean hasInput = !ShapeSupport.isUnit(op.getInputShape());
    boolean hasOutput = !ShapeSupport.isUnit(op.getOutputShape());
    String opName = CSharpNaming.typeName(op.getId().getName());
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

          if (hasOutput) {
            writer.write(
                "return await runtime.InvokeAsync($LBinding, $L,"
                    + " cancellationToken).ConfigureAwait(false);",
                opName,
                inputArg);
          } else {
            writer.write(
                "await runtime.InvokeAsync($LBinding, $L,"
                    + " cancellationToken).ConfigureAwait(false);",
                opName,
                inputArg);
            writer.write("return;");
          }
        });
    writer.write("");
  }

  private void writeEventStreamOperationMethod(SymbolProvider sp, Model model, OperationShape op) {
    if (!supportsEventStreamOperations()) {
      writer.write("public $L", operationSignature(sp, op));
      writer.openBlock(
          "{",
          "}",
          () ->
              writer.write(
                  "throw new System.NotSupportedException(\"Streaming operations are only wired"
                      + " for native gRPC clients today.\");"));
      writer.write("");
      return;
    }

    boolean inputStreaming = isInputStreaming(model, op);
    boolean outputStreaming = isOutputStreaming(model, op);
    boolean hasContainerInput = !ShapeSupport.isUnit(op.getInputShape()) && !inputStreaming;
    boolean hasContainerOutput = !ShapeSupport.isUnit(op.getOutputShape()) && !outputStreaming;
    String opName = CSharpNaming.typeName(op.getId().getName());
    String inputArg = hasContainerInput ? "input" : "SmithyUnit.Value";

    writer.write(outputStreaming ? "public $L" : "public async $L", operationSignature(sp, op));
    writer.openBlock(
        "{",
        "}",
        () -> {
          if (inputStreaming || hasContainerInput) {
            writer.write("System.ArgumentNullException.ThrowIfNull(input);");
          }

          if (outputStreaming) {
            if (hasContainerInput) {
              writeIdempotencyTokenDefaults(
                  model.expectShape(op.getInputShape(), StructureShape.class));
            }
            writer.write("return InvokeAsync();");
            writer.write("");
            writer.write(
                "async System.Collections.Generic.IAsyncEnumerable<$L> InvokeAsync()",
                streamingEventType(sp, model, op.getOutputShape()));
            writer.openBlock(
                "{",
                "}",
                () -> {
                  if (inputStreaming) {
                    writer.write(
                        "var request = $LProtocol.SerializeRequest(input, cancellationToken);",
                        opName);
                  } else {
                    writer.write(
                        "var request = $LProtocol.SerializeRequest($L);", opName, inputArg);
                  }
                  writer.write(
                      "var response = await eventStreamInvoker.InvokeAsync($L, $L, request,"
                          + " cancellationToken).ConfigureAwait(false);",
                      CSharpNaming.formatString(service.getId().getName()),
                      CSharpNaming.formatString(op.getId().getName()));
                  writer.write(
                      "await foreach (var item in"
                          + " $LProtocol.DeserializeResponseEventsAsync(response,"
                          + " cancellationToken).ConfigureAwait(false))",
                      opName);
                  writer.openBlock("{", "}", () -> writer.write("yield return item;"));
                });
          } else {
            writer.write(
                "var request = $LProtocol.SerializeRequest(input, cancellationToken);", opName);
            writer.write(
                "var response = await eventStreamInvoker.InvokeAsync($L, $L, request,"
                    + " cancellationToken).ConfigureAwait(false);",
                CSharpNaming.formatString(service.getId().getName()),
                CSharpNaming.formatString(op.getId().getName()));
            if (hasContainerOutput) {
              writer.write(
                  "return await $LProtocol.DeserializeResponseAsync(response,"
                      + " cancellationToken).ConfigureAwait(false);",
                  opName);
            } else {
              writer.write("return;");
            }
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

  // ---------------- paginators ----------------

  /**
   * Resolved pagination for an operation: present when the operation carries {@code @paginated}
   * (merged with the service-level defaults). Event-stream operations are never paginated.
   */
  private Optional<PaginationInfo> paginationInfo(OperationShape op) {
    if (isEventStreamOperation(context.model(), op)) {
      return Optional.empty();
    }
    return PaginatedIndex.of(context.model()).getPaginationInfo(service, op);
  }

  private String paginatorPagesSignature(SymbolProvider sp, OperationShape op) {
    Model model = context.model();
    String inputType =
        CSharpSymbolProvider.qualified(sp.toSymbol(model.expectShape(op.getInputShape())));
    String outputType =
        CSharpSymbolProvider.qualified(sp.toSymbol(model.expectShape(op.getOutputShape())));
    return "System.Collections.Generic.IAsyncEnumerable<"
        + outputType
        + "> "
        + CSharpNaming.typeName(op.getId().getName())
        + "PagesAsync("
        + inputType
        + " input, System.Threading.CancellationToken cancellationToken = default)";
  }

  /**
   * The items-level paginator signature, present when {@code @paginated} names an {@code items}
   * member that resolves to a list. Map-valued items are rare and not generated yet — the pages
   * paginator still covers them.
   */
  private Optional<String> paginatorItemsSignature(SymbolProvider sp, PaginationInfo info) {
    return paginatorItemElementType(sp, info)
        .map(
            elementType ->
                "System.Collections.Generic.IAsyncEnumerable<"
                    + elementType
                    + "> "
                    + CSharpNaming.typeName(info.getOperation().getId().getName())
                    + "ItemsAsync("
                    + CSharpSymbolProvider.qualified(
                        sp.toSymbol(
                            context.model().expectShape(info.getOperation().getInputShape())))
                    + " input, System.Threading.CancellationToken cancellationToken = default)");
  }

  private Optional<String> paginatorItemElementType(SymbolProvider sp, PaginationInfo info) {
    List<MemberShape> path = info.getItemsMemberPath();
    if (path.isEmpty()) {
      return Optional.empty();
    }
    var target = context.model().expectShape(path.get(path.size() - 1).getTarget());
    if (!target.isListShape()) {
      return Optional.empty();
    }
    var element = context.model().expectShape(target.asListShape().get().getMember().getTarget());
    return Optional.of(CSharpSymbolProvider.qualified(sp.toSymbol(element)));
  }

  /** Renders a null-safe property access chain for a member path, e.g. {@code output.A?.B}. */
  private static String memberPathExpr(String root, List<MemberShape> path) {
    StringBuilder expr = new StringBuilder(root);
    for (int i = 0; i < path.size(); i++) {
      expr.append(i == 0 ? "." : "?.")
          .append(CSharpNaming.propertyName(path.get(i).getMemberName()));
    }
    return expr.toString();
  }

  /**
   * Renders the paginator methods for one {@code @paginated} operation. Pages repeat the unary call
   * while the response carries a continuation token, so every page flows through the normal client
   * lifecycle (auth, retries, endpoint resolution, telemetry).
   */
  private void writePaginatorMethods(SymbolProvider sp, OperationShape op, PaginationInfo info) {
    String opName = CSharpNaming.typeName(op.getId().getName());
    String tokenProperty = CSharpNaming.propertyName(info.getInputTokenMember().getMemberName());
    String outputTokenExpr = memberPathExpr("output", info.getOutputTokenMemberPath());

    writer.write(
        "public async $L",
        paginatorPagesSignature(sp, op)
            .replace(
                "System.Threading.CancellationToken cancellationToken",
                "[System.Runtime.CompilerServices.EnumeratorCancellation]"
                    + " System.Threading.CancellationToken cancellationToken"));
    writer.openBlock(
        "{",
        "}",
        () -> {
          writer.write("System.ArgumentNullException.ThrowIfNull(input);");
          writer.write(
              "var output = await $LAsync(input, cancellationToken).ConfigureAwait(false);",
              opName);
          writer.write("yield return output;");
          writer.write("var token = $L;", outputTokenExpr);
          writer.write("while (token is not null)");
          writer.openBlock(
              "{",
              "}",
              () -> {
                writer.write("input = input with { $L = token };", tokenProperty);
                writer.write(
                    "output = await $LAsync(input, cancellationToken).ConfigureAwait(false);",
                    opName);
                writer.write("yield return output;");
                writer.write("token = $L;", outputTokenExpr);
              });
        });
    writer.write("");

    paginatorItemsSignature(sp, info)
        .ifPresent(
            signature -> {
              String itemsExpr = memberPathExpr("page", info.getItemsMemberPath());
              writer.write(
                  "public async $L",
                  signature.replace(
                      "System.Threading.CancellationToken cancellationToken",
                      "[System.Runtime.CompilerServices.EnumeratorCancellation]"
                          + " System.Threading.CancellationToken cancellationToken"));
              writer.openBlock(
                  "{",
                  "}",
                  () -> {
                    writer.write(
                        "await foreach (var page in $LPagesAsync(input,"
                            + " cancellationToken).ConfigureAwait(false))",
                        opName);
                    writer.openBlock(
                        "{",
                        "}",
                        () -> {
                          writer.write("var items = $L;", itemsExpr);
                          writer.write("if (items is null)");
                          writer.openBlock("{", "}", () -> writer.write("continue;"));
                          writer.write("foreach (var item in items.Values)");
                          writer.openBlock("{", "}", () -> writer.write("yield return item;"));
                        });
                  });
              writer.write("");
            });
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
    Model model = context.model();
    boolean inputStreaming = isInputStreaming(model, op);
    boolean outputStreaming = isOutputStreaming(model, op);
    boolean hasInput = !ShapeSupport.isUnit(op.getInputShape()) && !inputStreaming;
    boolean hasOutput = !ShapeSupport.isUnit(op.getOutputShape()) && !outputStreaming;
    String name = CSharpNaming.typeName(op.getId().getName()) + "Async";
    String inputType =
        inputStreaming
            ? "System.Collections.Generic.IAsyncEnumerable<"
                + streamingEventType(sp, model, op.getInputShape())
                + ">"
            : hasInput
                ? CSharpSymbolProvider.qualified(sp.toSymbol(model.expectShape(op.getInputShape())))
                : null;
    String outputType =
        outputStreaming
            ? streamingEventType(sp, model, op.getOutputShape())
            : hasOutput
                ? CSharpSymbolProvider.qualified(
                    sp.toSymbol(model.expectShape(op.getOutputShape())))
                : null;
    String returnType =
        outputStreaming
            ? "System.Collections.Generic.IAsyncEnumerable<" + outputType + ">"
            : hasOutput
                ? "System.Threading.Tasks.Task<" + outputType + ">"
                : "System.Threading.Tasks.Task";
    String params = inputStreaming || hasInput ? inputType + " input, " : "";
    return returnType
        + " "
        + name
        + "("
        + params
        + "System.Threading.CancellationToken cancellationToken = default)";
  }

  private boolean isEventStreamOperation(Model model, OperationShape op) {
    return isInputStreaming(model, op) || isOutputStreaming(model, op);
  }

  private boolean supportsEventStreamOperations() {
    return ProtocolSupport.declaredKinds(service).contains(Kind.GRPC);
  }

  private boolean isInputStreaming(Model model, OperationShape op) {
    return ShapeSupport.isEventStreamShape(model, op.getInputShape());
  }

  private boolean isOutputStreaming(Model model, OperationShape op) {
    return ShapeSupport.isEventStreamShape(model, op.getOutputShape());
  }

  private String streamingEventType(SymbolProvider sp, Model model, ShapeId shapeId) {
    ShapeId target =
        ShapeSupport.streamingMemberTarget(model, shapeId)
            .orElseThrow(() -> new IllegalStateException("Expected streaming shape: " + shapeId));
    return CSharpSymbolProvider.qualified(sp.toSymbol(model.expectShape(target)));
  }

  private String streamingEventSchema(Model model, ShapeId shapeId) {
    ShapeId target =
        ShapeSupport.streamingMemberTarget(model, shapeId)
            .orElseThrow(() -> new IllegalStateException("Expected streaming shape: " + shapeId));
    return SchemaGenerator.shapeSchemaAccessor(context, model.expectShape(target));
  }
}
