/*
 * Renders C# Kafka producer and consumer stubs for a @kafkaJson service.
 *
 * bote models a contract from the owner's perspective. A @kafkaJson service has
 * two kinds of operations, each carrying its topic on the trait itself:
 *
 *   @kafkaProduce — clients produce the operation's input (a @command structure)
 *                   to the topic. The contract owner consumes it.
 *   @kafkaConsume — the contract owner emits @event payloads (the members of the
 *                   output's @streaming union) to the topic. Clients consume them.
 *
 * Each generated artifact therefore serves both roles symmetrically:
 *
 *   {Service}Producer
 *     - {Op}Async(command)            one per @kafkaProduce — serialize the command
 *                                     payload and write it to the command topic
 *                                     (a client invoking a capability)
 *     - Publish{Event}Async(event)    one per @kafkaConsume union member — write
 *                                     the event to the event topic (the owner
 *                                     emitting)
 *
 *   I{Service}CommandHandler / {Service}CommandConsumer
 *     - consumes the @kafkaProduce topics, deserializes the bare command payload,
 *       dispatches to Handle{Op}Async (the owner handling commands)
 *
 *   I{Service}EventHandler / {Service}EventConsumer
 *     - consumes the @kafkaConsume topics, decodes each event per the protocol's
 *       eventDiscrimination, dispatches to Handle{Event}Async (a client handling
 *       events)
 *
 * Commands are written as the bare payload (one command type per topic). Event
 * serialization follows the kafkaJson protocol's eventDiscrimination setting:
 *
 *   ENVELOPE (default) — the value is union-wrapped ({"member": {...}})
 *   HEADER             — the value is the bare payload; a "bote-type" Kafka
 *                        header carries the union member name
 *   NONE               — the value is the bare payload; the channel carries a
 *                        single event type (validator-enforced)
 *
 * The topic is read from the @kafkaProduce / @kafkaConsume trait. Key (@kafkaKey)
 * and headers (@kafkaHeader) are read from the payload structure's members.
 */
package io.github.thomaslaich.nsmithy.csharp.codegen.generators;

import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpNaming;
import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpSymbolProvider;
import io.github.thomaslaich.nsmithy.csharp.codegen.GenerationContext;
import io.github.thomaslaich.nsmithy.csharp.codegen.RuntimeTypes;
import io.github.thomaslaich.nsmithy.csharp.codegen.TraitIds;
import io.github.thomaslaich.nsmithy.csharp.codegen.support.KafkaBindings;
import io.github.thomaslaich.nsmithy.csharp.codegen.support.ShapeSupport;
import io.github.thomaslaich.nsmithy.csharp.codegen.writer.CSharpWriter;
import java.util.Comparator;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Optional;
import java.util.Set;
import java.util.stream.Collectors;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.shapes.MemberShape;
import software.amazon.smithy.model.shapes.ServiceShape;
import software.amazon.smithy.model.shapes.Shape;
import software.amazon.smithy.model.shapes.ShapeType;
import software.amazon.smithy.model.shapes.StructureShape;
import software.amazon.smithy.utils.SmithyInternalApi;

@SmithyInternalApi
public final class KafkaGenerator implements Runnable {

  /** How JSON event messages carry their type on a multi-event channel. */
  private enum EventDiscrimination {
    ENVELOPE,
    HEADER,
    NONE
  }

  /** The Kafka header that carries the union member name in HEADER mode. */
  private static final String TYPE_HEADER = "bote-type";

  private final GenerationContext context;
  private final CSharpWriter writer;
  private final ServiceShape service;

  public KafkaGenerator(GenerationContext c, CSharpWriter w, ServiceShape s) {
    this.context = c;
    this.writer = w;
    this.service = s;
  }

  @Override
  public void run() {
    Model model = context.model();

    List<KafkaBindings.Produce> produces =
        KafkaBindings.produces(model, context.symbolProvider(), service);
    List<KafkaBindings.Consume> consumes =
        KafkaBindings.consumes(model, context.symbolProvider(), service);

    if (produces.isEmpty() && consumes.isEmpty()) return;

    EventDiscrimination discrimination = eventDiscrimination();

    writer.addImport(RuntimeTypes.NSMITHY_CORE_SERDE);
    writer.addImport(RuntimeTypes.NSMITHY_CODECS_JSON);
    writer.addImport(RuntimeTypes.CONFLUENT_KAFKA);
    writer.addImport(RuntimeTypes.SYSTEM_TEXT);
    writer.addImport(RuntimeTypes.SYSTEM_COLLECTIONS_GENERIC);

    String svc = CSharpNaming.typeName(service.getId().getName());

    writeProducer(svc, produces, consumes, model, discrimination);

    if (!produces.isEmpty()) {
      writer.write("");
      writeCommandHandlerInterface(svc, produces);
      writer.write("");
      writeCommandConsumer(svc, produces, model);
    }

    if (!consumes.isEmpty()) {
      writer.write("");
      writeEventHandlerInterface(svc, consumes, model);
      writer.write("");
      writeEventConsumer(svc, consumes, model, discrimination);
    }
  }

  // ===========================================================================
  // Producer
  // ===========================================================================

  private void writeProducer(
      String svc,
      List<KafkaBindings.Produce> produces,
      List<KafkaBindings.Consume> consumes,
      Model model,
      EventDiscrimination discrimination) {
    String typeName = svc + "Producer";
    writer.write("public sealed class $L : System.IAsyncDisposable", typeName);
    writer.openBlock(
        "{",
        "}",
        () -> {
          Set<Shape> codecShapes = new LinkedHashSet<>();
          for (KafkaBindings.Produce produce : produces) {
            codecShapes.add(produce.command());
          }
          codecShapes.addAll(eventCodecShapes(consumes, model, discrimination));
          codecShapes.forEach(this::writeCodecField);
          writer.write("private readonly IProducer<string?, byte[]> _producer;");
          writer.write("");
          writer.write("public $L(ProducerConfig config)", typeName);
          writer.openBlock(
              "{",
              "}",
              () ->
                  writer.write(
                      "_producer = new ProducerBuilder<string?, byte[]>(config ?? throw new"
                          + " System.ArgumentNullException(nameof(config))).Build();"));

          for (KafkaBindings.Produce produce : produces) {
            writer.write("");
            writeProduceMethod(produce, model);
          }

          for (KafkaBindings.Consume consume : consumes) {
            for (MemberShape member : consume.members()) {
              writer.write("");
              writePublishMethod(consume, member, model, discrimination);
            }
          }

          writer.write("");
          writer.write("public System.Threading.Tasks.ValueTask DisposeAsync()");
          writer.openBlock(
              "{",
              "}",
              () -> {
                writer.write("_producer.Dispose();");
                writer.write("return System.Threading.Tasks.ValueTask.CompletedTask;");
              });
        });
  }

  /** A client invoking a capability: serialize the bare command and write it to the topic. */
  private void writeProduceMethod(KafkaBindings.Produce produce, Model model) {
    writer.write(
        "public System.Threading.Tasks.Task $LAsync($L command,"
            + " System.Threading.CancellationToken cancellationToken = default)",
        produce.opName(),
        produce.commandType());
    writer.openBlock(
        "{",
        "}",
        () -> {
          writer.write("System.ArgumentNullException.ThrowIfNull(command);");
          writer.write("");
          writer.write(
              "var value = $L.Serialize(command);", codecFieldName(produce.commandType()));
          writeKeyHeadersAndProduce(model, produce.command(), "command", produce.topic(), null);
        });
  }

  /** The contract owner emitting an event: encode it per eventDiscrimination and write it. */
  private void writePublishMethod(
      KafkaBindings.Consume consume,
      MemberShape member,
      Model model,
      EventDiscrimination discrimination) {
    StructureShape event = model.expectShape(member.getTarget(), StructureShape.class);
    String eventType = qualified(model, member);
    String variant = CSharpNaming.typeName(member.getMemberName());
    writer.write(
        "public System.Threading.Tasks.Task Publish$LAsync($L message,"
            + " System.Threading.CancellationToken cancellationToken = default)",
        variant,
        eventType);
    writer.openBlock(
        "{",
        "}",
        () -> {
          writer.write("System.ArgumentNullException.ThrowIfNull(message);");
          writer.write("");
          switch (discrimination) {
            case ENVELOPE -> {
              writer.write("var wrapped = $L.From$L(message);", consume.unionType(), variant);
              writer.write(
                  "var value = $L.Serialize(wrapped);", codecFieldName(consume.unionType()));
              writeKeyHeadersAndProduce(model, event, "message", consume.topic(), null);
            }
            case HEADER -> {
              writer.write("var value = $L.Serialize(message);", codecFieldName(eventType));
              writeKeyHeadersAndProduce(
                  model, event, "message", consume.topic(), member.getMemberName());
            }
            case NONE -> {
              writer.write("var value = $L.Serialize(message);", codecFieldName(eventType));
              writeKeyHeadersAndProduce(model, event, "message", consume.topic(), null);
            }
          }
        });
  }

  /**
   * Emits the @kafkaKey / @kafkaHeader extraction, the Message, and the ProduceAsync call. Assumes
   * a local `var value` (the serialized payload bytes) is already in scope. When typeHeaderValue is
   * non-null, a "bote-type" header carrying it is always added (HEADER discrimination).
   */
  private void writeKeyHeadersAndProduce(
      Model model, StructureShape payload, String objExpr, String topic, String typeHeaderValue) {
    Optional<MemberShape> keyMember =
        payload.members().stream().filter(m -> m.hasTrait(TraitIds.KAFKA_KEY)).findFirst();
    if (keyMember.isPresent()) {
      writer.write("var key = $L;", keyExpression(model, keyMember.get(), objExpr));
    } else {
      writer.write("string? key = null;");
    }

    List<MemberShape> headerMembers =
        payload.members().stream()
            .filter(m -> m.hasTrait(TraitIds.KAFKA_HEADER))
            .sorted(Comparator.comparing(MemberShape::getMemberName))
            .collect(Collectors.toList());

    if (!headerMembers.isEmpty() || typeHeaderValue != null) {
      writer.write("var headers = new Headers();");
      if (typeHeaderValue != null) {
        writer.write(
            "headers.Add($L, Encoding.UTF8.GetBytes($L));",
            CSharpNaming.formatString(TYPE_HEADER),
            CSharpNaming.formatString(typeHeaderValue));
      }
      for (MemberShape hm : headerMembers) {
        String headerName = kafkaHeaderName(hm);
        String prop = CSharpNaming.propertyName(hm.getMemberName());
        String local = CSharpNaming.parameterName(hm.getMemberName());
        if (ShapeSupport.isNullable(hm)) {
          writer.write("if ($L.$L is { } $L)", objExpr, prop, local);
          writer.openBlock(
              "{",
              "}",
              () ->
                  writer.write(
                      "headers.Add($L, Encoding.UTF8.GetBytes($L));",
                      CSharpNaming.formatString(headerName),
                      headerValueExpression(model, hm, local)));
        } else {
          writer.write(
              "headers.Add($L, Encoding.UTF8.GetBytes($L));",
              CSharpNaming.formatString(headerName),
              headerValueExpression(model, hm, objExpr + "." + prop));
        }
      }
      writer.write(
          "var kafkaMessage = new Message<string?, byte[]> { Key = key, Value = value,"
              + " Headers = headers };");
    } else {
      writer.write("var kafkaMessage = new Message<string?, byte[]> { Key = key, Value = value };");
    }

    writer.write("");
    writer.write(
        "return _producer.ProduceAsync($L, kafkaMessage, cancellationToken);",
        CSharpNaming.formatString(topic));
  }

  // ===========================================================================
  // Command handling (@kafkaProduce)
  // ===========================================================================

  private void writeCommandHandlerInterface(String svc, List<KafkaBindings.Produce> produces) {
    writer.write("public interface I$LCommandHandler", svc);
    writer.openBlock(
        "{",
        "}",
        () -> {
          boolean first = true;
          for (KafkaBindings.Produce produce : produces) {
            if (!first) writer.write("");
            first = false;
            writer.write(
                "System.Threading.Tasks.Task Handle$LAsync($L command,"
                    + " System.Threading.CancellationToken cancellationToken = default);",
                produce.opName(),
                produce.commandType());
          }
        });
  }

  private void writeCommandConsumer(String svc, List<KafkaBindings.Produce> produces, Model model) {
    String typeName = svc + "CommandConsumer";
    String ifaceName = "I" + svc + "CommandHandler";
    List<String> topics =
        produces.stream()
            .map(KafkaBindings.Produce::topic)
            .distinct()
            .sorted()
            .collect(Collectors.toList());
    Set<Shape> codecShapes = new LinkedHashSet<>();
    for (KafkaBindings.Produce produce : produces) {
      codecShapes.add(produce.command());
    }
    writeConsumerScaffold(
        typeName,
        ifaceName,
        topics,
        () -> codecShapes.forEach(this::writeCodecField),
        () -> {
          for (int i = 0; i < produces.size(); i++) {
            KafkaBindings.Produce produce = produces.get(i);
            writer.write(
                "$L (result.Topic == $L)",
                i == 0 ? "if" : "else if",
                CSharpNaming.formatString(produce.topic()));
            writer.openBlock(
                "{",
                "}",
                () -> {
                  writer.write(
                      "var command = $L.Deserialize(result.Message.Value);",
                      codecFieldName(produce.commandType()));
                  writer.write("await _handler.Handle$LAsync(command, ct);", produce.opName());
                });
          }
        });
  }

  // ===========================================================================
  // Event handling (@kafkaConsume)
  // ===========================================================================

  private void writeEventHandlerInterface(
      String svc, List<KafkaBindings.Consume> consumes, Model model) {
    writer.write("public interface I$LEventHandler", svc);
    writer.openBlock(
        "{",
        "}",
        () -> {
          boolean first = true;
          for (KafkaBindings.Consume consume : consumes) {
            for (MemberShape member : consume.members()) {
              if (!first) writer.write("");
              first = false;
              writer.write(
                  "System.Threading.Tasks.Task Handle$LAsync($L message,"
                      + " System.Threading.CancellationToken cancellationToken = default);",
                  CSharpNaming.typeName(member.getMemberName()),
                  qualified(model, member));
            }
          }
        });
  }

  private void writeEventConsumer(
      String svc,
      List<KafkaBindings.Consume> consumes,
      Model model,
      EventDiscrimination discrimination) {
    String typeName = svc + "EventConsumer";
    String ifaceName = "I" + svc + "EventHandler";
    List<String> topics =
        consumes.stream()
            .map(KafkaBindings.Consume::topic)
            .distinct()
            .sorted()
            .collect(Collectors.toList());
    Set<Shape> codecShapes = eventCodecShapes(consumes, model, discrimination);
    writeConsumerScaffold(
        typeName,
        ifaceName,
        topics,
        () -> codecShapes.forEach(this::writeCodecField),
        () -> {
          for (int i = 0; i < consumes.size(); i++) {
            KafkaBindings.Consume consume = consumes.get(i);
            writer.write(
                "$L (result.Topic == $L)",
                i == 0 ? "if" : "else if",
                CSharpNaming.formatString(consume.topic()));
            writer.openBlock("{", "}", () -> writeEventDispatch(consume, model, discrimination));
          }
        });
  }

  /** Emits the per-topic event decode + dispatch body for the given discrimination mode. */
  private void writeEventDispatch(
      KafkaBindings.Consume consume, Model model, EventDiscrimination discrimination) {
    switch (discrimination) {
      case ENVELOPE -> {
        writer.write(
            "var union = $L.Deserialize(result.Message.Value);",
            codecFieldName(consume.unionType()));
        for (MemberShape member : consume.members()) {
          String variant = CSharpNaming.typeName(member.getMemberName());
          String local = CSharpNaming.parameterName(member.getMemberName());
          writer.write("if (union is $L.$L $L)", consume.unionType(), variant, local);
          writer.write("    await _handler.Handle$LAsync($L.Value, ct);", variant, local);
        }
      }
      case HEADER -> {
        writer.write(
            "if (result.Message.Headers is { } messageHeaders &&"
                + " messageHeaders.TryGetLastBytes($L, out var typeBytes))",
            CSharpNaming.formatString(TYPE_HEADER));
        writer.openBlock(
            "{",
            "}",
            () -> {
              writer.write("var eventType = Encoding.UTF8.GetString(typeBytes);");
              for (int i = 0; i < consume.members().size(); i++) {
                MemberShape member = consume.members().get(i);
                String variant = CSharpNaming.typeName(member.getMemberName());
                writer.write(
                    "$L (eventType == $L)",
                    i == 0 ? "if" : "else if",
                    CSharpNaming.formatString(member.getMemberName()));
                writer.openBlock(
                    "{",
                    "}",
                    () -> {
                      writer.write(
                          "var message = $L.Deserialize(result.Message.Value);",
                          codecFieldName(qualified(model, member)));
                      writer.write("await _handler.Handle$LAsync(message, ct);", variant);
                    });
              }
            });
      }
      case NONE -> {
        // NONE requires a single-member union (validator-enforced): the channel is
        // unambiguous by construction.
        MemberShape member = consume.members().get(0);
        String variant = CSharpNaming.typeName(member.getMemberName());
        writer.write(
            "var message = $L.Deserialize(result.Message.Value);",
            codecFieldName(qualified(model, member)));
        writer.write("await _handler.Handle$LAsync(message, ct);", variant);
      }
    }
  }

  // ===========================================================================
  // Shared consumer scaffold
  // ===========================================================================

  /**
   * Emits a consumer class with the codec fields (supplied by the caller), a constructor, a
   * Subscribe + consume loop that tolerates non-fatal ConsumeExceptions, a DispatchAsync (body
   * supplied by the caller), and DisposeAsync.
   */
  private void writeConsumerScaffold(
      String typeName,
      String ifaceName,
      List<String> topics,
      Runnable codecFields,
      Runnable dispatchBody) {
    String topicList =
        topics.stream().map(CSharpNaming::formatString).collect(Collectors.joining(", "));
    writer.write("public sealed class $L : System.IAsyncDisposable", typeName);
    writer.openBlock(
        "{",
        "}",
        () -> {
          codecFields.run();
          writer.write("private readonly IConsumer<string?, byte[]> _consumer;");
          writer.write("private readonly $L _handler;", ifaceName);
          writer.write("");
          writer.write("public $L(ConsumerConfig config, $L handler)", typeName, ifaceName);
          writer.openBlock(
              "{",
              "}",
              () -> {
                writer.write(
                    "_consumer = new ConsumerBuilder<string?, byte[]>(config ?? throw new"
                        + " System.ArgumentNullException(nameof(config))).Build();");
                writer.write(
                    "_handler = handler ?? throw new"
                        + " System.ArgumentNullException(nameof(handler));");
              });
          writer.write("");
          writer.write(
              "public async System.Threading.Tasks.Task RunAsync("
                  + "System.Threading.CancellationToken cancellationToken = default)");
          writer.openBlock(
              "{",
              "}",
              () -> {
                // Yield so the blocking Consume() loop runs on a thread-pool thread; this
                // lets callers do `var task = RunAsync(ct);` without the synchronous first
                // Consume() blocking them.
                writer.write("await System.Threading.Tasks.Task.Yield();");
                writer.write("_consumer.Subscribe([$L]);", topicList);
                writer.write("while (!cancellationToken.IsCancellationRequested)");
                writer.openBlock(
                    "{",
                    "}",
                    () -> {
                      writer.write("try");
                      writer.openBlock(
                          "{",
                          "}",
                          () -> {
                            writer.write("var result = _consumer.Consume(cancellationToken);");
                            writer.write("await DispatchAsync(result, cancellationToken);");
                          });
                      writer.write(
                          "catch (System.OperationCanceledException) when"
                              + " (cancellationToken.IsCancellationRequested) { throw; }");
                      writer.write("catch (ConsumeException e) when (!e.Error.IsFatal) { }");
                    });
              });
          writer.write("");
          writer.write(
              "private async System.Threading.Tasks.Task DispatchAsync(ConsumeResult<string?,"
                  + " byte[]> result, System.Threading.CancellationToken ct)");
          writer.openBlock("{", "}", dispatchBody);
          writer.write("");
          writer.write("public System.Threading.Tasks.ValueTask DisposeAsync()");
          writer.openBlock(
              "{",
              "}",
              () -> {
                writer.write("_consumer.Dispose();");
                writer.write("return System.Threading.Tasks.ValueTask.CompletedTask;");
              });
        });
  }

  // ===========================================================================
  // Trait / model helpers
  // ===========================================================================

  private String qualified(Model model, MemberShape member) {
    return CSharpSymbolProvider.qualified(
        context.symbolProvider().toSymbol(model.expectShape(member.getTarget())));
  }

  /** The name of the static per-type codec field, derived from the C# type's simple name. */
  private String codecFieldName(String qualifiedType) {
    int i = qualifiedType.lastIndexOf('.');
    return (i < 0 ? qualifiedType : qualifiedType.substring(i + 1)) + "Codec";
  }

  /** Emits one `private static readonly IJsonCodec<T> TCodec = JsonCodec.FromSchema(...)`. */
  private void writeCodecField(Shape shape) {
    String type = CSharpSymbolProvider.qualified(context.symbolProvider().toSymbol(shape));
    writer.write(
        "private static readonly IJsonCodec<$L> $L = JsonCodec.FromSchema($L.Schema);",
        type,
        codecFieldName(type),
        SchemaGenerator.schemaClassName(context, shape));
  }

  /** The payload shapes an event-side participant needs codecs for, per discrimination. */
  private Set<Shape> eventCodecShapes(
      List<KafkaBindings.Consume> consumes, Model model, EventDiscrimination discrimination) {
    Set<Shape> shapes = new LinkedHashSet<>();
    for (KafkaBindings.Consume consume : consumes) {
      if (discrimination == EventDiscrimination.ENVELOPE) {
        shapes.add(consume.union());
      } else {
        for (MemberShape member : consume.members()) {
          shapes.add(model.expectShape(member.getTarget()));
        }
      }
    }
    return shapes;
  }

  /** Reads the eventDiscrimination setting from the service's @kafkaJson trait. */
  private EventDiscrimination eventDiscrimination() {
    return service
        .findTrait(TraitIds.KAFKA_JSON)
        .map(t -> t.toNode().expectObjectNode())
        .flatMap(node -> node.getStringMember("eventDiscrimination"))
        .map(s -> EventDiscrimination.valueOf(s.getValue()))
        .orElse(EventDiscrimination.ENVELOPE);
  }

  private String kafkaHeaderName(MemberShape member) {
    return member
        .findTrait(TraitIds.KAFKA_HEADER)
        .map(t -> t.toNode().expectObjectNode())
        .flatMap(node -> node.getStringMember("name"))
        .map(s -> s.getValue())
        .orElseThrow(
            () ->
                new IllegalStateException("@kafkaHeader missing name on member " + member.getId()));
  }

  private String keyExpression(Model model, MemberShape member, String objectExpr) {
    String prop = objectExpr + "." + CSharpNaming.propertyName(member.getMemberName());
    ShapeType type = model.expectShape(member.getTarget()).getType();
    boolean nullable = ShapeSupport.isNullable(member);
    return switch (type) {
      case STRING -> prop;
      case ENUM -> nullable ? prop + "?.Value" : prop + ".Value";
      default -> nullable ? prop + "?.ToString()" : prop + ".ToString()";
    };
  }

  private String headerValueExpression(Model model, MemberShape member, String valueExpr) {
    ShapeType type = model.expectShape(member.getTarget()).getType();
    return switch (type) {
      case STRING -> valueExpr;
      case ENUM -> valueExpr + ".Value";
      default -> valueExpr + ".ToString()";
    };
  }
}
