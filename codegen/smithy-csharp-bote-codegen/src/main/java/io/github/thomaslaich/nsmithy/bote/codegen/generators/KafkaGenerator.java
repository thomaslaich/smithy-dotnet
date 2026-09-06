/** Generates contract bindings and thin service-role APIs for @kafkaJson services. */
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
    var produces = KafkaBindings.produces(model, context.symbolProvider(), service);
    var consumes = KafkaBindings.consumes(model, context.symbolProvider(), service);
    if (produces.isEmpty() && consumes.isEmpty()) return;
    writer.addImport(RuntimeTypes.NSMITHY_CORE_SERDE);
    writer.addImport(RuntimeTypes.NSMITHY_CODECS_JSON);
    writer.addImport("NSmithy.Messaging");
    writer.addImport(RuntimeTypes.SYSTEM_TEXT);
    writer.addImport(RuntimeTypes.SYSTEM_COLLECTIONS_GENERIC);
    String svc = CSharpNaming.typeName(service.getId().getName());
    writeRole(svc, "Client", produces, List.of());
    writeRole(svc, "EventPublisher", List.of(), consumes);
    for (var produce : produces) writeHandler(produce.opName(), produce.commandType());
    for (var consume : consumes) writeHandler(consume.opName(), consume.unionType());
    writer.write("internal static class $LMessaging", svc);
    writer.openBlock(
        "{",
        "}",
        () -> {
          Set<Shape> shapes = new LinkedHashSet<>();
          produces.forEach(p -> shapes.add(p.command()));
          shapes.addAll(eventCodecShapes(consumes, model));
          shapes.forEach(this::writePayloadCodecField);
          shapes.stream()
              .map(Shape::asStructureShape)
              .flatMap(Optional::stream)
              .filter(this::hasHeaderMembers)
              .forEach(this::writeHeaderDeserializer);
          for (var produce : produces) {
            writeSendBinding(
                produce.opName(), produce.commandType(), produce.operationId(), produce.topic());
            writer.write(
                "private static MessagePayload Encode$L($L command)",
                produce.opName(),
                produce.commandType());
            writer.openBlock(
                "{",
                "}",
                () -> {
                  writer.write(
                      "var value = $L.Serialize(command);", codecFieldName(produce.commandType()));
                  writeEncodedPayload(model, produce.command(), "command", null);
                });
            writeReceiveBinding(
                produce.opName(), produce.commandType(), produce.operationId(), produce.topic());
            writer.write(
                "private static $L Decode$L(MessagePayload payload)",
                produce.commandType(),
                produce.opName());
            writer.openBlock(
                "{",
                "}",
                () -> {
                  writePayloadDeserialization(
                      produce.command(), "command", "payload.Value", "payload.Headers");
                  writer.write("return command;");
                });
          }
          for (var consume : consumes) {
            for (var member : consume.members()) {
              String name = "Publish" + CSharpNaming.typeName(member.getMemberName());
              String type = qualified(model, member);
              writeSendBinding(name, type, consume.operationId(), consume.topic());
              writer.write("private static MessagePayload Encode$L($L message)", name, type);
              writer.openBlock(
                  "{",
                  "}",
                  () -> {
                    if (eventDiscrimination() == EventDiscrimination.ENVELOPE)
                      writer.write(
                          "var value = WrapEvent($L, $L.Serialize(message));",
                          CSharpNaming.formatString(eventWireName(member)),
                          codecFieldName(type));
                    else writer.write("var value = $L.Serialize(message);", codecFieldName(type));
                    writeEncodedPayload(
                        model,
                        model.expectShape(member.getTarget(), StructureShape.class),
                        "message",
                        eventDiscrimination() == EventDiscrimination.HEADER
                            ? member.getMemberName()
                            : null);
                  });
            }
            writeReceiveBinding(
                consume.opName(), consume.unionType(), consume.operationId(), consume.topic());
            writer.write(
                "private static $L Decode$L(MessagePayload payload)",
                consume.unionType(),
                consume.opName());
            writer.openBlock("{", "}", () -> writeEventDecode(consume, model));
          }
          if (!consumes.isEmpty() && eventDiscrimination() == EventDiscrimination.ENVELOPE)
            writeEnvelopeWrapper();
        });
  }

  private void writeHandler(String operation, String type) {
    writer.write("public interface I$LHandler", operation);
    writer.openBlock(
        "{",
        "}",
        () ->
            writer.write(
                "System.Threading.Tasks.Task HandleAsync($L message,"
                    + " System.Threading.CancellationToken cancellationToken = default);",
                type));
  }

  private void writeRole(
      String svc,
      String role,
      List<KafkaBindings.Produce> produces,
      List<KafkaBindings.Consume> consumes) {
    if (produces.isEmpty() && consumes.isEmpty()) return;
    writer.write("public interface I$L$L", svc, role);
    writer.openBlock(
        "{",
        "}",
        () -> {
          for (var produce : produces)
            writeMethodSignature(produce.opName(), produce.commandType(), false);
          for (var consume : consumes)
            for (var member : consume.members())
              writeMethodSignature(
                  "Publish" + CSharpNaming.typeName(member.getMemberName()),
                  qualified(context.model(), member),
                  false);
        });
    writer.write("public sealed class $L$L : I$L$L", svc, role, svc, role);
    writer.openBlock(
        "{",
        "}",
        () -> {
          writer.write("private readonly IMessageSender _sender;");
          writer.write("public $L$L(IMessageSender sender)", svc, role);
          writer.openBlock(
              "{",
              "}",
              () ->
                  writer.write(
                      "_sender = sender ?? throw new"
                          + " System.ArgumentNullException(nameof(sender));"));
          for (var produce : produces)
            writeSendMethod(svc, produce.opName(), produce.commandType());
          for (var consume : consumes)
            for (var member : consume.members())
              writeSendMethod(
                  svc,
                  "Publish" + CSharpNaming.typeName(member.getMemberName()),
                  qualified(context.model(), member));
        });
  }

  private void writeMethodSignature(String name, String type, boolean implementation) {
    writer.write(
        "$LSystem.Threading.Tasks.Task $LAsync($L message, System.Threading.CancellationToken"
            + " cancellationToken = default)$L",
        implementation ? "public " : "",
        name,
        type,
        implementation ? "" : ";");
  }

  private void writeSendMethod(String svc, String name, String type) {
    writeMethodSignature(name, type, true);
    writer.openBlock(
        "{",
        "}",
        () ->
            writer.write(
                "return _sender.SendAsync($LMessaging.$LSend, message, cancellationToken);",
                svc,
                name));
  }

  private void writeSendBinding(String name, String type, String operation, String topic) {
    writer.write(
        "internal static readonly MessageSendBinding<$L> $LSend = new($L, $L, $L, Encode$L);",
        type,
        name,
        CSharpNaming.formatString(service.getId().toString()),
        CSharpNaming.formatString(operation),
        CSharpNaming.formatString(topic),
        name);
  }

  private void writeReceiveBinding(String name, String type, String operation, String topic) {
    writer.write(
        "internal static readonly MessageReceiveBinding<$L, I$LHandler> $LReceive = new($L, $L, $L,"
            + " Decode$L, static (handler, message, ct) => handler.HandleAsync(message, ct));",
        type,
        name,
        name,
        CSharpNaming.formatString(service.getId().toString()),
        CSharpNaming.formatString(operation),
        CSharpNaming.formatString(topic),
        name);
  }

  private void writeEventDecode(KafkaBindings.Consume consume, Model model) {
    if (eventDiscrimination() == EventDiscrimination.ENVELOPE) {
      writer.write("using var envelope = System.Text.Json.JsonDocument.Parse(payload.Value);");
      writer.write("if (envelope.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object)");
      writer.write(
          "    throw new System.Text.Json.JsonException(\"Expected an event envelope object.\");");
      writer.write("var properties = envelope.RootElement.EnumerateObject();");
      writer.write(
          "if (!properties.MoveNext()) throw new System.Text.Json.JsonException(\"Expected one"
              + " event envelope member.\");");
      writer.write("var property = properties.Current;");
      writer.write(
          "if (properties.MoveNext()) throw new System.Text.Json.JsonException(\"Expected one event"
              + " envelope member.\");");
      writer.write("var eventType = property.Name;");
      writer.write("var eventValue = Encoding.UTF8.GetBytes(property.Value.GetRawText());");
    } else if (eventDiscrimination() == EventDiscrimination.HEADER) {
      writer.write(
          "if (payload.Headers is null || !payload.Headers.TryGetValue(\"bote-type\", out var"
              + " typeBytes))");
      writer.write(
          "    throw new System.Text.Json.JsonException(\"Missing bote-type event header.\");");
      writer.write("var eventType = Encoding.UTF8.GetString(typeBytes);");
    }
    for (var member : consume.members()) {
      Runnable decode =
          () -> {
            writePayloadDeserialization(
                model.expectShape(member.getTarget(), StructureShape.class),
                "message",
                eventDiscrimination() == EventDiscrimination.ENVELOPE
                    ? "eventValue"
                    : "payload.Value",
                "payload.Headers");
            writer.write(
                "return $L.From$L(message);",
                consume.unionType(),
                CSharpNaming.typeName(member.getMemberName()));
          };
      if (eventDiscrimination() == EventDiscrimination.NONE) decode.run();
      else {
        writer.write(
            "if (eventType == $L)",
            CSharpNaming.formatString(
                eventDiscrimination() == EventDiscrimination.ENVELOPE
                    ? eventWireName(member)
                    : member.getMemberName()));
        writer.openBlock("{", "}", decode);
      }
    }
    if (eventDiscrimination() != EventDiscrimination.NONE)
      writer.write(
          "throw new System.Text.Json.JsonException(\"Unknown event type: \" + eventType);");
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
  private void writeEncodedPayload(
      Model model, StructureShape payload, String objExpr, String typeHeaderValue) {
    Optional<MemberShape> keyMember =
        payload.members().stream().filter(m -> m.hasTrait(TraitIds.KAFKA_KEY)).findFirst();
    if (keyMember.isPresent()) {
      writer.write("var key = $L;", keyExpression(model, keyMember.get(), objExpr));
    } else {
      writer.write("string? key = null;");
    }

    List<MemberShape> headerMembers = headerMembers(payload);

    if (!headerMembers.isEmpty() || typeHeaderValue != null) {
      writer.write("var headers = new Dictionary<string, byte[]>();");
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
    } else {
      writer.write("Dictionary<string, byte[]>? headers = null;");
    }
    writer.write("return new MessagePayload(value, key, headers);");
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
        "private static $L $L(byte[] value, IReadOnlyDictionary<string, byte[]>? headers)",
        type,
        deserializeMethodName(type));
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
                  "if (headers is null || !headers.TryGetValue($L, out var $L))",
                  CSharpNaming.formatString(headerName),
                  bytes);
              writer.write(
                  "    throw new MissingRequiredMemberException($L);",
                  CSharpNaming.formatString(member.getMemberName()));
              writeHeaderAssignment(member, bytes);
            } else {
              writer.write(
                  "if (headers is { } && headers.TryGetValue($L, out var $L))",
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
