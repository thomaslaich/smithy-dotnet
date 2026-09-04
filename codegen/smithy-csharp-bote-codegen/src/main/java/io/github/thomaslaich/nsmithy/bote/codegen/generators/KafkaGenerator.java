/** Generates typed Kafka producers, consumers, and handlers for @kafkaJson services. */
package io.github.thomaslaich.nsmithy.bote.codegen.generators;

import io.github.thomaslaich.nsmithy.bote.codegen.RuntimeTypes;
import io.github.thomaslaich.nsmithy.bote.codegen.TraitIds;
import io.github.thomaslaich.nsmithy.bote.codegen.support.KafkaBindings;
import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpNaming;
import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpSymbolProvider;
import io.github.thomaslaich.nsmithy.csharp.codegen.GenerationContext;
import io.github.thomaslaich.nsmithy.csharp.codegen.generators.SchemaGenerator;
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
import software.amazon.smithy.model.traits.JsonNameTrait;
import software.amazon.smithy.utils.SmithyInternalApi;

@SmithyInternalApi
public final class KafkaGenerator implements Runnable {

  private enum EventDiscrimination {
    ENVELOPE,
    HEADER,
    NONE
  }

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
      writeCommandConsumer(svc, produces);
    }

    if (!consumes.isEmpty()) {
      writer.write("");
      writeEventHandlerInterface(svc, consumes, model);
      writer.write("");
      writeEventConsumer(svc, consumes, model, discrimination);
    }
  }

  // Producer

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
          codecShapes.addAll(eventCodecShapes(consumes, model));
          codecShapes.forEach(this::writePayloadCodecField);
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

          if (!consumes.isEmpty() && discrimination == EventDiscrimination.ENVELOPE) {
            writer.write("");
            writeEnvelopeWrapper();
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
          writer.write("var value = $L.Serialize(command);", codecFieldName(produce.commandType()));
          writeKeyHeadersAndProduce(model, produce.command(), "command", produce.topic(), null);
        });
  }

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
              writer.write(
                  "var value = WrapEvent($L, $L.Serialize(message));",
                  CSharpNaming.formatString(eventWireName(member)),
                  codecFieldName(eventType));
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

  private void writeEnvelopeWrapper() {
    writer.write("private static byte[] WrapEvent(string eventType, byte[] payload)");
    writer.openBlock(
        "{",
        "}",
        () -> {
          writer.write("using var stream = new System.IO.MemoryStream();");
          writer.write("using var json = new System.Text.Json.Utf8JsonWriter(stream);");
          writer.write("json.WriteStartObject();");
          writer.write("json.WritePropertyName(eventType);");
          writer.write("json.WriteRawValue(payload);");
          writer.write("json.WriteEndObject();");
          writer.write("json.Flush();");
          writer.write("return stream.ToArray();");
        });
  }

  // Expects the serialized payload in `value`; typeHeaderValue adds event discrimination.
  private void writeKeyHeadersAndProduce(
      Model model, StructureShape payload, String objExpr, String topic, String typeHeaderValue) {
    Optional<MemberShape> keyMember =
        payload.members().stream().filter(m -> m.hasTrait(TraitIds.KAFKA_KEY)).findFirst();
    if (keyMember.isPresent()) {
      writer.write("var key = $L;", keyExpression(model, keyMember.get(), objExpr));
    } else {
      writer.write("string? key = null;");
    }

    List<MemberShape> headerMembers = headerMembers(payload);

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
                      "headers.Add($L, $L);",
                      CSharpNaming.formatString(headerName),
                      headerBytesExpression(model, hm, local)));
        } else {
          writer.write(
              "headers.Add($L, $L);",
              CSharpNaming.formatString(headerName),
              headerBytesExpression(model, hm, objExpr + "." + prop));
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

  // Command handling (@kafkaProduce)

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

  private void writeCommandConsumer(String svc, List<KafkaBindings.Produce> produces) {
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
        () -> {
          codecShapes.forEach(this::writePayloadCodecField);
          codecShapes.stream()
              .map(Shape::asStructureShape)
              .flatMap(Optional::stream)
              .filter(this::hasHeaderMembers)
              .forEach(this::writeHeaderDeserializer);
        },
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
                  writePayloadDeserialization(
                      produce.command(),
                      "command",
                      "result.Message.Value",
                      "result.Message.Headers");
                  writer.write("await _handler.Handle$LAsync(command, ct);", produce.opName());
                });
          }
        });
  }

  // Event handling (@kafkaConsume)

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
    Set<Shape> codecShapes = eventCodecShapes(consumes, model);
    writeConsumerScaffold(
        typeName,
        ifaceName,
        topics,
        () -> {
          codecShapes.forEach(this::writePayloadCodecField);
          codecShapes.stream()
              .map(Shape::asStructureShape)
              .flatMap(Optional::stream)
              .filter(this::hasHeaderMembers)
              .forEach(this::writeHeaderDeserializer);
        },
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

  private void writeEventDispatch(
      KafkaBindings.Consume consume, Model model, EventDiscrimination discrimination) {
    switch (discrimination) {
      case ENVELOPE -> {
        writer.write(
            "using var envelope = System.Text.Json.JsonDocument.Parse(result.Message.Value);");
        writer.write(
            "if (envelope.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object)");
        writer.write(
            "    throw new System.Text.Json.JsonException(\"Expected an event envelope"
                + " object.\");");
        writer.write("var properties = envelope.RootElement.EnumerateObject();");
        writer.write("if (!properties.MoveNext())");
        writer.write(
            "    throw new System.Text.Json.JsonException(\"Expected one event envelope"
                + " member.\");");
        writer.write("var property = properties.Current;");
        writer.write("if (properties.MoveNext())");
        writer.write(
            "    throw new System.Text.Json.JsonException(\"Expected one event envelope"
                + " member.\");");
        writer.write("var eventValue = Encoding.UTF8.GetBytes(property.Value.GetRawText());");
        for (int i = 0; i < consume.members().size(); i++) {
          MemberShape member = consume.members().get(i);
          String variant = CSharpNaming.typeName(member.getMemberName());
          StructureShape event = model.expectShape(member.getTarget(), StructureShape.class);
          writer.write(
              "$L (property.Name == $L)",
              i == 0 ? "if" : "else if",
              CSharpNaming.formatString(eventWireName(member)));
          writer.openBlock(
              "{",
              "}",
              () -> {
                writePayloadDeserialization(
                    event, "message", "eventValue", "result.Message.Headers");
                writer.write("await _handler.Handle$LAsync(message, ct);", variant);
              });
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
                      writePayloadDeserialization(
                          model.expectShape(member.getTarget(), StructureShape.class),
                          "message",
                          "result.Message.Value",
                          "result.Message.Headers");
                      writer.write("await _handler.Handle$LAsync(message, ct);", variant);
                    });
              }
            });
      }
      case NONE -> {
        // The validator limits NONE to one event type.
        MemberShape member = consume.members().get(0);
        String variant = CSharpNaming.typeName(member.getMemberName());
        writePayloadDeserialization(
            model.expectShape(member.getTarget(), StructureShape.class),
            "message",
            "result.Message.Value",
            "result.Message.Headers");
        writer.write("await _handler.Handle$LAsync(message, ct);", variant);
      }
    }
  }

  // Shared consumer scaffold
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
                writer.write("System.ArgumentNullException.ThrowIfNull(config);");
                writer.write("var consumerConfig = new Dictionary<string, string>();");
                writer.write("foreach (var entry in config)");
                writer.write("    consumerConfig[entry.Key] = entry.Value;");
                writer.write("consumerConfig[\"enable.auto.commit\"] = \"true\";");
                writer.write("consumerConfig[\"enable.auto.offset.store\"] = \"false\";");
                writer.write(
                    "_consumer = new ConsumerBuilder<string?, byte[]>(consumerConfig).Build();");
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
                // Prevent the first blocking Consume() from blocking the caller synchronously.
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
                            writer.write("_consumer.StoreOffset(result);");
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

  // Trait / model helpers

  private String qualified(Model model, MemberShape member) {
    return CSharpSymbolProvider.qualified(
        context.symbolProvider().toSymbol(model.expectShape(member.getTarget())));
  }

  private String codecFieldName(String qualifiedType) {
    int i = qualifiedType.lastIndexOf('.');
    return (i < 0 ? qualifiedType : qualifiedType.substring(i + 1)) + "Codec";
  }

  // Header-bound members use a body projection because they never appear in JSON.
  private void writePayloadCodecField(Shape shape) {
    String type = CSharpSymbolProvider.qualified(context.symbolProvider().toSymbol(shape));
    Optional<StructureShape> structure = shape.asStructureShape();
    if (structure.isPresent() && hasHeaderMembers(structure.get())) {
      String schema = SchemaGenerator.schemaClassName(context, shape);
      String builder = schema + ".Builder";
      String structSchemaField = structSchemaFieldName(type);
      String excluded =
          headerMembers(structure.get()).stream()
              .map(member -> "member.Name != " + CSharpNaming.formatString(member.getMemberName()))
              .collect(Collectors.joining(" && "));
      writer.write(
          "private static readonly IStructSchema<$L, $L> $L ="
              + " (IStructSchema<$L, $L>)$L.Schema;",
          type,
          builder,
          structSchemaField,
          type,
          builder,
          schema);
      writer.write(
          "private static readonly IProjectionCodec<$L, $L> $L ="
              + " JsonCodecFactory.Default.FromProjection(Schemas.Project($L, member => $L));",
          type,
          builder,
          codecFieldName(type),
          structSchemaField,
          excluded);
      return;
    }
    writer.write(
        "private static readonly ICodec<$L> $L = JsonCodecFactory.Default.FromSchema($L.Schema);",
        type,
        codecFieldName(type),
        SchemaGenerator.schemaClassName(context, shape));
  }

  private Set<Shape> eventCodecShapes(List<KafkaBindings.Consume> consumes, Model model) {
    Set<Shape> shapes = new LinkedHashSet<>();
    for (KafkaBindings.Consume consume : consumes) {
      for (MemberShape member : consume.members()) {
        shapes.add(model.expectShape(member.getTarget()));
      }
    }
    return shapes;
  }

  private void writePayloadDeserialization(
      StructureShape payload, String local, String valueExpr, String headersExpr) {
    String type = CSharpSymbolProvider.qualified(context.symbolProvider().toSymbol(payload));
    if (hasHeaderMembers(payload)) {
      writer.write(
          "var $L = $L($L, $L);", local, deserializeMethodName(type), valueExpr, headersExpr);
    } else {
      writer.write("var $L = $L.Deserialize($L);", local, codecFieldName(type), valueExpr);
    }
  }

  private void writeHeaderDeserializer(StructureShape payload) {
    String type = CSharpSymbolProvider.qualified(context.symbolProvider().toSymbol(payload));
    String structSchemaField = structSchemaFieldName(type);
    writer.write("");
    writer.write(
        "private static $L $L(byte[] value, Headers? headers)", type, deserializeMethodName(type));
    writer.openBlock(
        "{",
        "}",
        () -> {
          writer.write("var builder = $L.CreateTypedBuilder();", structSchemaField);
          writer.write("$L.ReadInto(value, builder);", codecFieldName(type));
          for (MemberShape member : headerMembers(payload)) {
            String bytes = CSharpNaming.parameterName(member.getMemberName()) + "Bytes";
            String headerName = kafkaHeaderName(member);
            if (ShapeSupport.isRequired(member)) {
              writer.write(
                  "if (headers is null || !headers.TryGetLastBytes($L, out var $L))",
                  CSharpNaming.formatString(headerName),
                  bytes);
              writer.write(
                  "    throw new MissingRequiredMemberException($L);",
                  CSharpNaming.formatString(member.getMemberName()));
              writeHeaderAssignment(member, bytes);
            } else {
              writer.write(
                  "if (headers is { } && headers.TryGetLastBytes($L, out var $L))",
                  CSharpNaming.formatString(headerName),
                  bytes);
              writer.openBlock("{", "}", () -> writeHeaderAssignment(member, bytes));
            }
          }
          writer.write("return $L.Build(builder);", structSchemaField);
        });
  }

  private void writeHeaderAssignment(MemberShape member, String bytesExpression) {
    Shape target = context.model().expectShape(member.getTarget());
    String prop = CSharpNaming.propertyName(member.getMemberName());
    if (target.getType() == ShapeType.BLOB) {
      writer.write("builder.$L = $L;", prop, bytesExpression);
      return;
    }
    String text = CSharpNaming.parameterName(member.getMemberName()) + "Text";
    writer.write("var $L = Encoding.UTF8.GetString($L);", text, bytesExpression);
    writer.write("builder.$L = $L;", prop, headerParseExpression(target, text));
  }

  private List<MemberShape> headerMembers(StructureShape payload) {
    return payload.members().stream()
        .filter(member -> member.hasTrait(TraitIds.KAFKA_HEADER))
        .sorted(Comparator.comparing(MemberShape::getMemberName))
        .collect(Collectors.toList());
  }

  private boolean hasHeaderMembers(StructureShape payload) {
    return payload.members().stream().anyMatch(member -> member.hasTrait(TraitIds.KAFKA_HEADER));
  }

  private String structSchemaFieldName(String qualifiedType) {
    int i = qualifiedType.lastIndexOf('.');
    return (i < 0 ? qualifiedType : qualifiedType.substring(i + 1)) + "StructSchema";
  }

  private String deserializeMethodName(String qualifiedType) {
    int i = qualifiedType.lastIndexOf('.');
    return "Deserialize" + (i < 0 ? qualifiedType : qualifiedType.substring(i + 1));
  }

  private String eventWireName(MemberShape member) {
    return member
        .getTrait(JsonNameTrait.class)
        .map(JsonNameTrait::getValue)
        .orElse(member.getMemberName());
  }

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

  private String headerBytesExpression(Model model, MemberShape member, String valueExpr) {
    Shape target = model.expectShape(member.getTarget());
    if (target.getType() == ShapeType.BLOB) return valueExpr;
    return "Encoding.UTF8.GetBytes(" + headerTextExpression(target, valueExpr) + ")";
  }

  private String headerTextExpression(Shape target, String valueExpr) {
    return switch (target.getType()) {
      case STRING -> valueExpr;
      case ENUM -> valueExpr + ".Value";
      case INT_ENUM ->
          "((int)" + valueExpr + ").ToString(System.Globalization.CultureInfo.InvariantCulture)";
      case BOOLEAN -> valueExpr + " ? \"true\" : \"false\"";
      case FLOAT, DOUBLE ->
          valueExpr + ".ToString(\"R\", System.Globalization.CultureInfo.InvariantCulture)";
      case TIMESTAMP ->
          valueExpr
              + ".ToUniversalTime().ToString(\"O\","
              + " System.Globalization.CultureInfo.InvariantCulture)";
      case BYTE, SHORT, INTEGER, LONG, BIG_INTEGER, BIG_DECIMAL ->
          valueExpr + ".ToString(null, System.Globalization.CultureInfo.InvariantCulture)";
      default ->
          throw new IllegalArgumentException(
              "@kafkaHeader only supports simple types and blobs; "
                  + target.getId()
                  + " is "
                  + target.getType());
    };
  }

  private String headerParseExpression(Shape target, String textExpr) {
    String type = CSharpSymbolProvider.qualified(context.symbolProvider().toSymbol(target));
    String invariant = "System.Globalization.CultureInfo.InvariantCulture";
    return switch (target.getType()) {
      case STRING -> textExpr;
      case ENUM -> "new " + type + "(" + textExpr + ")";
      case INT_ENUM -> "(" + type + ")int.Parse(" + textExpr + ", " + invariant + ")";
      case BOOLEAN -> "bool.Parse(" + textExpr + ")";
      case BYTE -> "sbyte.Parse(" + textExpr + ", " + invariant + ")";
      case SHORT -> "short.Parse(" + textExpr + ", " + invariant + ")";
      case INTEGER -> "int.Parse(" + textExpr + ", " + invariant + ")";
      case LONG -> "long.Parse(" + textExpr + ", " + invariant + ")";
      case FLOAT -> "float.Parse(" + textExpr + ", " + invariant + ")";
      case DOUBLE -> "double.Parse(" + textExpr + ", " + invariant + ")";
      case BIG_INTEGER -> "System.Numerics.BigInteger.Parse(" + textExpr + ", " + invariant + ")";
      case BIG_DECIMAL -> "decimal.Parse(" + textExpr + ", " + invariant + ")";
      case TIMESTAMP ->
          "System.DateTimeOffset.Parse("
              + textExpr
              + ", "
              + invariant
              + ", System.Globalization.DateTimeStyles.RoundtripKind)";
      default ->
          throw new IllegalArgumentException(
              "@kafkaHeader only supports simple types and blobs; "
                  + target.getId()
                  + " is "
                  + target.getType());
    };
  }
}
