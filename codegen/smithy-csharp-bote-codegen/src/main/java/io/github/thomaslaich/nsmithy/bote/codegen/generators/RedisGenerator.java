package io.github.thomaslaich.nsmithy.bote.codegen.generators;

import io.github.thomaslaich.nsmithy.bote.codegen.RuntimeTypes;
import io.github.thomaslaich.nsmithy.bote.codegen.support.RedisBindings;
import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpNaming;
import io.github.thomaslaich.nsmithy.csharp.codegen.GenerationContext;
import io.github.thomaslaich.nsmithy.csharp.codegen.generators.SchemaGenerator;
import io.github.thomaslaich.nsmithy.csharp.codegen.writer.CSharpWriter;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Optional;
import java.util.Set;
import java.util.stream.Collectors;
import software.amazon.smithy.codegen.core.CodegenException;
import software.amazon.smithy.model.shapes.MemberShape;
import software.amazon.smithy.model.shapes.ServiceShape;
import software.amazon.smithy.model.shapes.Shape;
import software.amazon.smithy.model.shapes.StructureShape;

/** Generates operation facts and typed roles; Redis behavior belongs to the runtime. */
public final class RedisGenerator implements Runnable {
  public enum Kind {
    STREAMS,
    PUB_SUB
  }

  private final GenerationContext context;
  private final CSharpWriter writer;
  private final ServiceShape service;
  private final Kind kind;

  private record Command(
      String operationId,
      String name,
      String address,
      Shape shape,
      String type,
      Optional<StructureShape> reply,
      Optional<String> replyType,
      Optional<Long> maxLength) {}

  public RedisGenerator(
      GenerationContext context, CSharpWriter writer, ServiceShape service, Kind kind) {
    this.context = context;
    this.writer = writer;
    this.service = service;
    this.kind = kind;
  }

  @Override
  public void run() {
    if (!context.settings().generateClient() && !context.settings().generateServer()) return;
    var commands =
        kind == Kind.STREAMS
            ? RedisBindings.streamAdds(context.model(), context.symbolProvider(), service).stream()
                .map(
                    c ->
                        new Command(
                            c.operationId(),
                            c.opName(),
                            c.stream(),
                            c.command(),
                            c.commandType(),
                            c.reply(),
                            c.replyType(),
                            c.maxLen()))
                .toList()
            : RedisBindings.publishes(context.model(), context.symbolProvider(), service).stream()
                .map(
                    c ->
                        new Command(
                            c.operationId(),
                            c.opName(),
                            c.channel(),
                            c.command(),
                            c.commandType(),
                            Optional.<StructureShape>empty(),
                            Optional.<String>empty(),
                            Optional.<Long>empty()))
                .toList();
    var events =
        kind == Kind.STREAMS
            ? RedisBindings.streamReads(context.model(), context.symbolProvider(), service)
            : RedisBindings.subscribes(context.model(), context.symbolProvider(), service);
    if (commands.isEmpty() && events.isEmpty()) return;
    validateAddresses(
        commands.stream().map(Command::address).toList(),
        events.stream().map(RedisBindings.Subscription::address).toList(),
        "Redis address");
    writer.addImport(RuntimeTypes.NSMITHY_CORE_SERDE);
    writer.addImport(RuntimeTypes.NSMITHY_CODECS_JSON);
    writer.addImport("NSmithy.Messaging");
    writer.addImport("NSmithy.Messaging.Redis");
    String svc = CSharpNaming.typeName(service.getId().getName());
    if (!commands.isEmpty()) writeClient(svc, commands);
    if (!events.isEmpty()) writePublisher(svc, events);
    for (var c : commands) writeHandler(c.name(), c.type(), c.replyType());
    for (var e : events) writeHandler(e.opName(), e.unionType(), Optional.empty());
    writer.write("internal static class $LMessaging", svc);
    writer.openBlock(
        "{",
        "}",
        () -> {
          Set<Shape> shapes = new LinkedHashSet<>();
          for (var c : commands) {
            shapes.add(c.shape());
            c.reply().ifPresent(shapes::add);
          }
          for (var e : events) shapes.add(e.union());
          shapes.forEach(this::writeCodecField);
          for (var c : commands) writeCommandBindings(c);
          for (var e : events) writeEventBindings(e);
        });
    if (context.settings().generateDependencyInjection()) writeRegistration(svc, commands, events);
  }

  private static String task(Optional<String> reply) {
    return "System.Threading.Tasks.Task" + reply.map(r -> "<" + r + ">").orElse("");
  }

  private void writeHandler(String name, String type, Optional<String> reply) {
    writer.write("public interface I$LHandler", name);
    writer.openBlock(
        "{",
        "}",
        () ->
            writer.write(
                "$L HandleAsync($L message, System.Threading.CancellationToken cancellationToken ="
                    + " default);",
                task(reply),
                type));
  }

  private void writeClient(String svc, List<Command> commands) {
    writer.write("public interface I$LClient", svc);
    writer.openBlock(
        "{",
        "}",
        () -> {
          for (var c : commands)
            writer.write(
                "$L $LAsync($L message, System.Threading.CancellationToken cancellationToken ="
                    + " default);",
                task(c.replyType()),
                c.name(),
                c.type());
        });
    writer.write("public sealed class $LClient : I$LClient", svc, svc);
    writer.openBlock(
        "{",
        "}",
        () -> {
          boolean sends = commands.stream().anyMatch(c -> c.reply().isEmpty());
          boolean requests = commands.stream().anyMatch(c -> c.reply().isPresent());
          if (sends) writer.write("private readonly IMessageSender _sender;");
          if (requests) writer.write("private readonly IMessageRequestSender _requests;");
          String parameters =
              (sends ? "IMessageSender sender" : "")
                  + (sends && requests ? ", " : "")
                  + (requests ? "IMessageRequestSender requests" : "");
          writer.write("public $LClient($L)", svc, parameters);
          writer.openBlock(
              "{",
              "}",
              () -> {
                if (sends)
                  writer.write(
                      "_sender = sender ?? throw new"
                          + " System.ArgumentNullException(nameof(sender));");
                if (requests)
                  writer.write(
                      "_requests = requests ?? throw new"
                          + " System.ArgumentNullException(nameof(requests));");
              });
          for (var c : commands) {
            writer.write(
                "public $L $LAsync($L message, System.Threading.CancellationToken cancellationToken"
                    + " = default)",
                task(c.replyType()),
                c.name(),
                c.type());
            writer.openBlock(
                "{",
                "}",
                () ->
                    writer.write(
                        "return $L($LMessaging.$LSend, message, cancellationToken);",
                        c.reply().isPresent() ? "_requests.RequestAsync" : "_sender.SendAsync",
                        svc,
                        c.name()));
          }
        });
  }

  private void writePublisher(String svc, List<RedisBindings.Subscription> events) {
    writer.write("public interface I$LEventPublisher", svc);
    writer.openBlock(
        "{",
        "}",
        () -> {
          for (var e : events)
            for (var member : e.members())
              writer.write(
                  "System.Threading.Tasks.Task Publish$LAsync($L message,"
                      + " System.Threading.CancellationToken cancellationToken = default);",
                  CSharpNaming.typeName(member.getMemberName()),
                  qualified(member));
        });
    writer.write("public sealed class $LEventPublisher : I$LEventPublisher", svc, svc);
    writer.openBlock(
        "{",
        "}",
        () -> {
          writer.write("private readonly IMessageSender _sender;");
          writer.write("public $LEventPublisher(IMessageSender sender)", svc);
          writer.openBlock(
              "{",
              "}",
              () ->
                  writer.write(
                      "_sender = sender ?? throw new"
                          + " System.ArgumentNullException(nameof(sender));"));
          for (var e : events)
            for (var member : e.members()) {
              String name = CSharpNaming.typeName(member.getMemberName());
              writer.write(
                  "public System.Threading.Tasks.Task Publish$LAsync($L message,"
                      + " System.Threading.CancellationToken cancellationToken = default)",
                  name,
                  qualified(member));
              writer.openBlock(
                  "{",
                  "}",
                  () ->
                      writer.write(
                          "return _sender.SendAsync($LMessaging.Publish$LSend, message,"
                              + " cancellationToken);",
                          svc,
                          name));
            }
        });
  }

  private void writeCommandBindings(Command c) {
    String facts =
        literal(service.getId().toString())
            + ", "
            + literal(c.operationId())
            + ", "
            + literal(c.address());
    String encode =
        "static message => new MessagePayload(" + codecFieldName(c.type()) + ".Serialize(message))";
    String decode = "static payload => " + codecFieldName(c.type()) + ".Deserialize(payload.Value)";
    if (c.replyType().isPresent()) {
      String reply = c.replyType().orElseThrow();
      writer.write(
          "internal static readonly RedisStreamRequestBinding<$L, $L> $LSend = new($L, $L, static"
              + " payload => $L.Deserialize(payload.Value), $L);",
          c.type(),
          reply,
          c.name(),
          facts,
          encode,
          codecFieldName(reply),
          c.maxLength().map(Object::toString).orElse("null"));
      writer.write(
          "internal static readonly MessageReplyReceiveBinding<$L, $L, I$LHandler> $LReceive ="
              + " new($L, $L, static reply => new MessagePayload($L.Serialize(reply)), static"
              + " (handler, message, ct) => handler.HandleAsync(message, ct));",
          c.type(),
          reply,
          c.name(),
          c.name(),
          facts,
          decode,
          codecFieldName(reply));
    } else {
      writer.write(
          "internal static readonly $L<$L> $LSend = new($L, $L$L);",
          kind == Kind.STREAMS ? "RedisStreamSendBinding" : "MessageSendBinding",
          c.type(),
          c.name(),
          facts,
          encode,
          kind == Kind.STREAMS ? ", " + c.maxLength().map(Object::toString).orElse("null") : "");
      writer.write(
          "internal static readonly MessageReceiveBinding<$L, I$LHandler> $LReceive = new($L, $L,"
              + " static (handler, message, ct) => handler.HandleAsync(message, ct));",
          c.type(),
          c.name(),
          c.name(),
          facts,
          decode);
    }
  }

  private void writeEventBindings(RedisBindings.Subscription e) {
    String facts =
        literal(service.getId().toString())
            + ", "
            + literal(e.operationId())
            + ", "
            + literal(e.address());
    for (var member : e.members()) {
      String name = CSharpNaming.typeName(member.getMemberName());
      writer.write(
          "internal static readonly $L<$L> Publish$LSend = new($L, static message => new"
              + " MessagePayload($L.Serialize($L.From$L(message)))$L);",
          kind == Kind.STREAMS ? "RedisStreamSendBinding" : "MessageSendBinding",
          qualified(member),
          name,
          facts,
          codecFieldName(e.unionType()),
          e.unionType(),
          name,
          kind == Kind.STREAMS ? ", " + e.maxLen().map(Object::toString).orElse("null") : "");
    }
    writer.write(
        "internal static readonly MessageReceiveBinding<$L, I$LHandler> $LReceive = new($L, static"
            + " payload => $L.Deserialize(payload.Value), static (handler, message, ct) =>"
            + " handler.HandleAsync(message, ct));",
        e.unionType(),
        e.opName(),
        e.opName(),
        facts,
        codecFieldName(e.unionType()));
  }

  private void writeRegistration(
      String svc, List<Command> commands, List<RedisBindings.Subscription> events) {
    writer.addImport("Microsoft.Extensions.DependencyInjection");
    writer.addImport("Microsoft.Extensions.DependencyInjection.Extensions");
    writer.write("public static class $LMessagingExtensions", svc);
    writer.openBlock(
        "{",
        "}",
        () -> {
          if (!commands.isEmpty()) {
            writeRoleRegistration(svc, "Client");
            writeConsumerRegistration(
                svc, "Command", commands.stream().map(Command::name).toList());
          }
          if (!events.isEmpty()) {
            writeRoleRegistration(svc, "EventPublisher");
            writeConsumerRegistration(
                svc, "Event", events.stream().map(RedisBindings.Subscription::opName).toList());
          }
        });
  }

  private void writeRoleRegistration(String svc, String role) {
    writer.write(
        "public static IServiceCollection Add$L$L(this IServiceCollection services)", svc, role);
    writer.openBlock(
        "{",
        "}",
        () -> {
          writer.write("services.TryAddSingleton<I$L$L, $L$L>();", svc, role, svc, role);
          writer.write("return services;");
        });
  }

  private void writeConsumerRegistration(String svc, String role, List<String> operations) {
    String transport = kind == Kind.STREAMS ? "RedisStream" : "RedisPubSub";
    writer.write(
        "public static IServiceCollection Add$L$LConsumer(this IServiceCollection services,"
            + " $LConsumerOptions? options = null)",
        svc,
        role,
        transport);
    writer.openBlock(
        "{",
        "}",
        () ->
            writer.write(
                "return services.Add$LConsumer(options, $L);",
                transport,
                operations.stream()
                    .map(op -> svc + "Messaging." + op + "Receive")
                    .collect(Collectors.joining(", "))));
  }

  private void writeCodecField(Shape shape) {
    String type = context.symbolProvider().toSymbol(shape).getFullName();
    writer.write(
        "private static readonly ICodec<$L> $L = JsonCodecFactory.Default.FromSchema($L.Schema);",
        type,
        codecFieldName(type),
        SchemaGenerator.schemaClassName(context, shape));
  }

  private String qualified(MemberShape member) {
    StructureShape shape = context.model().expectShape(member.getTarget(), StructureShape.class);
    return context.symbolProvider().toSymbol(shape).getFullName();
  }

  private static String codecFieldName(String qualifiedType) {
    return qualifiedType.replace('.', '_').replace('?', '_') + "Codec";
  }

  private static String literal(String value) {
    return CSharpNaming.formatString(value);
  }

  private static void validateAddresses(
      List<String> inbound, List<String> outbound, String addressKind) {
    Set<String> inboundUnique = new LinkedHashSet<>(inbound);
    Set<String> outboundUnique = new LinkedHashSet<>(outbound);
    if (inboundUnique.size() != inbound.size()) {
      throw new CodegenException(addressKind + " has more than one command operation.");
    }
    if (outboundUnique.size() != outbound.size()) {
      throw new CodegenException(addressKind + " has more than one event operation.");
    }
    inboundUnique.retainAll(outboundUnique);
    if (!inboundUnique.isEmpty()) {
      throw new CodegenException(
          addressKind
              + " cannot carry both commands and events without a direction discriminator: "
              + inboundUnique);
    }
  }
}
