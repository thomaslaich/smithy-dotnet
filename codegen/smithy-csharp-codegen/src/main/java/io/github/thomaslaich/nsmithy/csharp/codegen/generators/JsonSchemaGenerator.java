package io.github.thomaslaich.nsmithy.csharp.codegen.generators;

import java.nio.charset.StandardCharsets;
import java.util.HexFormat;
import java.util.Map;
import java.util.stream.Collectors;
import software.amazon.smithy.jsonschema.JsonSchemaConfig;
import software.amazon.smithy.jsonschema.JsonSchemaConverter;
import software.amazon.smithy.jsonschema.JsonSchemaMapper;
import software.amazon.smithy.jsonschema.JsonSchemaMapperContext;
import software.amazon.smithy.jsonschema.JsonSchemaVersion;
import software.amazon.smithy.jsonschema.Schema;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.loader.Prelude;
import software.amazon.smithy.model.neighbor.Walker;
import software.amazon.smithy.model.node.ArrayNode;
import software.amazon.smithy.model.node.Node;
import software.amazon.smithy.model.node.NodeVisitor;
import software.amazon.smithy.model.node.ObjectNode;
import software.amazon.smithy.model.shapes.Shape;
import software.amazon.smithy.model.shapes.ShapeId;
import software.amazon.smithy.model.traits.TimestampFormatTrait;
import software.amazon.smithy.model.transform.ModelTransformer;
import software.amazon.smithy.utils.SmithyInternalApi;

/** Generates the canonical JSON document representation used by NSmithy's JSON codecs. */
@SmithyInternalApi
final class JsonSchemaGenerator {

  private JsonSchemaGenerator() {}

  static String generate(Model model, ShapeId rootShape) {
    Shape root = model.expectShape(rootShape);
    var closure = new Walker(model).walkShapes(root);
    ModelTransformer transformer = ModelTransformer.create();
    Model scopedModel = transformer.filterShapes(model, closure::contains);
    Map<ShapeId, ShapeId> renamedShapes =
        scopedModel
            .shapes()
            .filter(shape -> !shape.isMemberShape())
            .filter(shape -> !Prelude.isPreludeShape(shape))
            .collect(
                Collectors.toMap(
                    Shape::getId,
                    shape ->
                        ShapeId.fromParts(
                            shape.getId().getNamespace(), uniqueDefinitionName(shape.getId()))));
    scopedModel = transformer.renameShapes(scopedModel, renamedShapes);
    ShapeId normalizedRoot = renamedShapes.getOrDefault(rootShape, rootShape);

    JsonSchemaConfig config = new JsonSchemaConfig();
    config.setJsonSchemaVersion(JsonSchemaVersion.DRAFT2020_12);
    config.setUseJsonName(true);
    config.setDefaultTimestampFormat(TimestampFormatTrait.Format.EPOCH_SECONDS);
    config.setUseIntegerType(true);
    config.setAddReferenceDescriptions(true);
    config.setSchemaDocumentExtensions(
        Node.objectNodeBuilder()
            .withMember("$schema", "https://json-schema.org/draft/2020-12/schema")
            .build());

    Node schema =
        JsonSchemaConverter.builder()
            .model(scopedModel)
            .rootShape(normalizedRoot)
            .config(config)
            .addMapper(new NsmithyJsonMapper())
            .build()
            .convert()
            .toNode();
    return Node.printJson(schema.accept(CloseObjectSchemas.INSTANCE));
  }

  private static String uniqueDefinitionName(ShapeId id) {
    String namespace = HexFormat.of().formatHex(id.getNamespace().getBytes(StandardCharsets.UTF_8));
    return "N" + namespace + "_" + id.getName();
  }

  /** Aligns details where NSmithy's canonical JSON representation is stricter than the default. */
  private static final class NsmithyJsonMapper implements JsonSchemaMapper {
    @Override
    public Schema.Builder updateSchema(JsonSchemaMapperContext context, Schema.Builder builder) {
      Shape shape = context.getShape();
      Shape target =
          shape
              .asMemberShape()
              .map(member -> context.getModel().expectShape(member.getTarget()))
              .orElse(shape);

      if (target.isStructureShape()) {
        builder.additionalProperties(Schema.builder().trivial(false).build());
      }

      return switch (target.getType()) {
        case BYTE -> builder.minimum(Byte.MIN_VALUE).maximum(Byte.MAX_VALUE);
        case SHORT -> builder.minimum(Short.MIN_VALUE).maximum(Short.MAX_VALUE);
        case INTEGER -> builder.minimum(Integer.MIN_VALUE).maximum(Integer.MAX_VALUE);
        case LONG -> builder.minimum(Long.MIN_VALUE).maximum(Long.MAX_VALUE);
        case BIG_INTEGER -> builder.type("integer");
        case BLOB -> builder.contentEncoding("base64");
        default -> builder;
      };
    }
  }

  /** Closes anonymous union alternatives, which do not pass through a shape mapper. */
  private static final class CloseObjectSchemas extends NodeVisitor.Default<Node> {
    private static final CloseObjectSchemas INSTANCE = new CloseObjectSchemas();

    @Override
    protected Node getDefault(Node node) {
      return node;
    }

    @Override
    public Node arrayNode(ArrayNode node) {
      ArrayNode.Builder builder = ArrayNode.builder();
      node.getElements().forEach(value -> builder.withValue(value.accept(this)));
      return builder.build();
    }

    @Override
    public Node objectNode(ObjectNode node) {
      ObjectNode.Builder builder = ObjectNode.builder();
      node.getMembers().forEach((key, value) -> builder.withMember(key, value.accept(this)));
      boolean objectWithProperties =
          node.getStringMember("type").map(value -> value.getValue().equals("object")).orElse(false)
              && node.containsMember("properties");
      if (objectWithProperties && !node.containsMember("additionalProperties")) {
        builder.withMember("additionalProperties", false);
      }
      return builder.build();
    }
  }
}
