/*
 * Emits proto3 .proto file(s) for a service annotated with alloy's @grpc trait.
 *
 * Conventions:
 *   - Every member of a structure/error/union targeted by the service must carry
 *     an alloy @protoIndex trait (1-based field number).
 *   - The service file is placed at {outputNamespaceDir}/{ServiceName}.proto.
 *   - Shapes from other Smithy namespaces are collected into per-namespace
 *     types.proto files placed at {foreignOutputNamespaceDir}/types.proto. The service file imports
 *     them and references their types by fully-qualified proto name.
 *   - TIMESTAMP maps to google.protobuf.Timestamp (not int64).
 *   - DOCUMENT maps to google.protobuf.Value.
 *   - @sparse maps use google.protobuf.Value as the value type.
 *   - Unions become `oneof` wrapped in a message, unless @protoInlinedOneOf is
 *     present, in which case the oneof is inlined into the parent message.
 *   - IntEnum 0-value conflict is avoided: _UNSPECIFIED = 0 is only emitted when
 *     no existing value already occupies field number 0.
 *   - @protoNumType maps integer members to sint/uint/fixed proto types.
 *   - Streaming operations use the `stream` keyword in rpc signatures.
 *   - Invalid proto3 map key types (float, double, bytes, message, etc.) cause a
 *     CodegenException at code-generation time.
 *   - Duplicate @protoIndex values within a message/union cause a CodegenException.
 */
package io.github.thomaslaich.nsmithy.proto.codegen;

import java.util.ArrayList;
import java.util.Comparator;
import java.util.HashSet;
import java.util.LinkedHashMap;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.Optional;
import java.util.Set;
import java.util.TreeMap;
import java.util.stream.Collectors;
import software.amazon.smithy.build.FileManifest;
import software.amazon.smithy.codegen.core.CodegenException;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.knowledge.TopDownIndex;
import software.amazon.smithy.model.node.Node;
import software.amazon.smithy.model.node.ObjectNode;
import software.amazon.smithy.model.node.StringNode;
import software.amazon.smithy.model.shapes.EnumShape;
import software.amazon.smithy.model.shapes.IntEnumShape;
import software.amazon.smithy.model.shapes.ListShape;
import software.amazon.smithy.model.shapes.MapShape;
import software.amazon.smithy.model.shapes.MemberShape;
import software.amazon.smithy.model.shapes.OperationShape;
import software.amazon.smithy.model.shapes.ServiceShape;
import software.amazon.smithy.model.shapes.Shape;
import software.amazon.smithy.model.shapes.ShapeId;
import software.amazon.smithy.model.shapes.ShapeType;
import software.amazon.smithy.model.shapes.StructureShape;
import software.amazon.smithy.model.shapes.UnionShape;
import software.amazon.smithy.model.traits.DefaultTrait;
import software.amazon.smithy.model.traits.RequiredTrait;
import software.amazon.smithy.model.traits.SparseTrait;
import software.amazon.smithy.model.traits.StreamingTrait;
import software.amazon.smithy.utils.SmithyInternalApi;

@SmithyInternalApi
public final class ProtoGenerator {

  // Well-known Smithy shape IDs
  private static final ShapeId UNIT = ShapeId.from("smithy.api#Unit");

  // Alloy trait IDs
  private static final ShapeId GRPC = ShapeId.from("alloy.proto#grpc");
  private static final ShapeId PROTO_INDEX = ShapeId.from("alloy.proto#protoIndex");
  private static final ShapeId PROTO_NUM_TYPE = ShapeId.from("alloy.proto#protoNumType");
  private static final ShapeId PROTO_INLINED_ONE_OF = ShapeId.from("alloy.proto#protoInlinedOneOf");

  // Well-known proto import paths
  private static final String IMPORT_EMPTY = "google/protobuf/empty.proto";
  private static final String IMPORT_TIMESTAMP = "google/protobuf/timestamp.proto";
  private static final String IMPORT_VALUE = "google/protobuf/struct.proto";

  private final Model model;
  private final ServiceShape service;
  private final String baseNamespace;
  private final ObjectNode fileOptions;
  private final FileManifest fileManifest;

  public ProtoGenerator(
      Model model,
      ServiceShape service,
      String baseNamespace,
      ObjectNode fileOptions,
      FileManifest fileManifest) {
    this.model = model;
    this.service = service;
    this.baseNamespace = baseNamespace;
    this.fileOptions = fileOptions;
    this.fileManifest = fileManifest;
  }

  // ---- entry point -------------------------------------------------------

  public void run() {
    if (!service.hasTrait(GRPC)) return;

    TopDownIndex idx = TopDownIndex.of(model);
    List<OperationShape> operations =
        idx.getContainedOperations(service).stream()
            .sorted(Comparator.comparing(o -> o.getId().toString()))
            .toList();

    // Collect referenced shapes and group by Smithy namespace.
    Set<ShapeId> allReferenced = collectReferencedShapes(operations);
    String serviceNs = service.getId().getNamespace();

    Map<String, List<ShapeId>> byNamespace =
        allReferenced.stream()
            .collect(
                Collectors.groupingBy(
                    id -> model.expectShape(id).getId().getNamespace(),
                    TreeMap::new,
                    Collectors.toList()));

    // Generate one types.proto per foreign namespace and record import paths.
    Map<String, String> nsToImportPath = new LinkedHashMap<>();
    for (var entry : byNamespace.entrySet()) {
      String ns = entry.getKey();
      if (ns.equals(serviceNs)) continue;
      String foreignOutputNamespace = outputNamespaceFor(ns);
      String importPath = foreignOutputNamespace.replace('.', '/') + "/types.proto";
      nsToImportPath.put(ns, importPath);
      generateTypesFile(ns, importPath, entry.getValue());
    }

    // Generate the main service file.
    List<ShapeId> localShapes =
        byNamespace.getOrDefault(serviceNs, List.of()).stream()
            .sorted(Comparator.comparing(ShapeId::toString))
            .toList();
    generateServiceFile(operations, localShapes, nsToImportPath);
  }

  // ---- file generators ---------------------------------------------------

  /**
   * Emits the service .proto file containing the gRPC service definition and all shapes that belong
   * to the service's own Smithy namespace.
   */
  private void generateServiceFile(
      List<OperationShape> operations,
      List<ShapeId> localShapes,
      Map<String, String> nsToImportPath) {

    String serviceNs = service.getId().getNamespace();
    String serviceName = ProtoNaming.typeName(service.getId().getName());
    String outputNamespace = outputNamespaceFor(serviceNs);
    String path = outputNamespace.replace('.', '/') + "/" + serviceName + ".proto";

    // Determine required well-known imports.
    Set<String> imports = new LinkedHashSet<>(nsToImportPath.values());
    boolean needsEmpty =
        operations.stream()
            .anyMatch(op -> isUnit(op.getInputShape()) || isUnit(op.getOutputShape()));
    if (needsEmpty) imports.add(IMPORT_EMPTY);
    if (shapesNeedTimestamp(localShapes)) imports.add(IMPORT_TIMESTAMP);
    if (shapesNeedValue(localShapes)) imports.add(IMPORT_VALUE);
    // Also scan operation I/O for well-known types used directly.
    for (OperationShape op : operations) {
      checkWellKnownImports(op.getInputShape(), imports, serviceNs);
      checkWellKnownImports(op.getOutputShape(), imports, serviceNs);
    }

    StringBuilder sb = new StringBuilder();
    writeFileHeader(sb, serviceNs, imports);

    // Service block.
    sb.append("service ").append(serviceName).append(" {\n");
    for (OperationShape op : operations) {
      ShapeId inputMessage = streamingMemberTarget(op.getInputShape()).orElse(op.getInputShape());
      ShapeId outputMessage =
          streamingMemberTarget(op.getOutputShape()).orElse(op.getOutputShape());
      String inputRef = messageRef(inputMessage, serviceNs);
      String outputRef = messageRef(outputMessage, serviceNs);
      String streamIn = isStreamingShape(op.getInputShape()) ? "stream " : "";
      String streamOut = isStreamingShape(op.getOutputShape()) ? "stream " : "";
      sb.append("  rpc ").append(ProtoNaming.typeName(op.getId().getName())).append(" (");
      sb.append(streamIn).append(inputRef);
      sb.append(") returns (");
      sb.append(streamOut).append(outputRef);
      sb.append(");\n");
    }
    sb.append("}\n\n");

    // Local shape definitions.
    writeShapes(sb, localShapes, serviceNs);

    fileManifest.writeFile(path, sb.toString());
  }

  /**
   * Emits a types.proto file for shapes from a non-service Smithy namespace. No gRPC service block
   * — only message/enum definitions.
   */
  private void generateTypesFile(String smithyNamespace, String path, List<ShapeId> shapes) {

    Set<String> imports = new LinkedHashSet<>();
    if (shapesNeedTimestamp(shapes)) imports.add(IMPORT_TIMESTAMP);
    if (shapesNeedValue(shapes)) imports.add(IMPORT_VALUE);

    StringBuilder sb = new StringBuilder();
    writeFileHeader(sb, smithyNamespace, imports);

    List<ShapeId> sorted = shapes.stream().sorted(Comparator.comparing(ShapeId::toString)).toList();
    writeShapes(sb, sorted, smithyNamespace);

    fileManifest.writeFile(path, sb.toString());
  }

  // ---- common helpers ----------------------------------------------------

  private void writeFileHeader(StringBuilder sb, String protoPackage, Set<String> imports) {
    sb.append("// <auto-generated />\n");
    sb.append("// Generated by smithy-proto-codegen. DO NOT EDIT.\n");
    sb.append("syntax = \"proto3\";\n\n");
    sb.append("package ").append(protoPackage).append(";\n\n");
    for (Map.Entry<StringNode, Node> option : fileOptions.getMembers().entrySet()) {
      sb.append("option ")
          .append(option.getKey().getValue())
          .append(" = ")
          .append(fileOptionValue(option.getValue(), protoPackage))
          .append(";\n");
    }
    if (!fileOptions.getMembers().isEmpty()) sb.append("\n");
    for (String imp : imports) {
      sb.append("import \"").append(imp).append("\";\n");
    }
    if (!imports.isEmpty()) sb.append("\n");
  }

  private String outputNamespaceFor(String smithyNamespace) {
    return ProtoNaming.namespaceFor(smithyNamespace, baseNamespace);
  }

  private String fileOptionValue(Node value, String smithyNamespace) {
    if (value.isStringNode()) {
      return quote(value.expectStringNode().getValue());
    }
    if (value.isBooleanNode()) {
      return Boolean.toString(value.expectBooleanNode().getValue());
    }
    if (value.isNumberNode()) {
      return value.expectNumberNode().getValue().toString();
    }
    if (value.isObjectNode()) {
      return quote(derivedNamespace(value.expectObjectNode(), smithyNamespace));
    }
    throw new IllegalArgumentException("Unsupported proto file option value: " + value);
  }

  private String derivedNamespace(ObjectNode config, String smithyNamespace) {
    String prefix = config.getStringMember("prefix").map(s -> s.getValue()).orElse(baseNamespace);
    String suffix = config.getStringMember("suffix").map(s -> s.getValue()).orElse("");
    String namespaceCase = config.getStringMember("case").map(s -> s.getValue()).orElse("preserve");
    String namespace =
        switch (namespaceCase) {
          case "pascal" -> ProtoNaming.namespaceFor(smithyNamespace, prefix);
          case "lower" -> appendNamespace(prefix, smithyNamespace.toLowerCase(Locale.ROOT));
          case "preserve" -> appendNamespace(prefix, smithyNamespace);
          default ->
              throw new IllegalArgumentException("Unsupported namespace case: " + namespaceCase);
        };
    return appendNamespace(namespace, suffix);
  }

  private static String appendNamespace(String prefix, String suffix) {
    if (prefix == null || prefix.isBlank()) return suffix;
    if (suffix == null || suffix.isBlank()) return prefix;
    return prefix + "." + suffix;
  }

  private static String quote(String value) {
    return "\"" + value.replace("\\", "\\\\").replace("\"", "\\\"") + "\"";
  }

  private void writeShapes(StringBuilder sb, List<ShapeId> shapeIds, String localNs) {
    for (ShapeId id : shapeIds) {
      Shape s = model.expectShape(id);
      switch (s.getType()) {
        case ENUM -> writeEnum(sb, (EnumShape) s);
        case INT_ENUM -> writeIntEnum(sb, (IntEnumShape) s);
        case STRUCTURE -> writeMessage(sb, (StructureShape) s, localNs);
        case UNION -> {
          UnionShape u = (UnionShape) s;
          // @protoInlinedOneOf unions have no wrapper message — they are
          // written inline into parent structures by writeMessage.
          if (!u.hasTrait(PROTO_INLINED_ONE_OF)) {
            writeUnionMessage(sb, u, localNs);
          }
        }
        default -> {
          /* lists/maps are expressed inline at field sites */
        }
      }
    }
  }

  // ---- shape collection --------------------------------------------------

  private Set<ShapeId> collectReferencedShapes(List<OperationShape> operations) {
    Set<ShapeId> result = new LinkedHashSet<>();
    Set<ShapeId> seen = new HashSet<>();
    for (OperationShape op : operations) {
      collect(op.getInputShape(), result, seen);
      collect(op.getOutputShape(), result, seen);
      for (ShapeId errId : op.getErrors(service)) collect(errId, result, seen);
    }
    return result;
  }

  private void collect(ShapeId id, Set<ShapeId> result, Set<ShapeId> seen) {
    if (isUnit(id) || !seen.add(id)) return;
    Shape s = model.expectShape(id);
    switch (s.getType()) {
      case STRUCTURE, UNION, ENUM, INT_ENUM -> {
        result.add(id);
        for (MemberShape m : s.members()) collect(m.getTarget(), result, seen);
      }
      case LIST, SET -> collect(((ListShape) s).getMember().getTarget(), result, seen);
      case MAP -> {
        collect(((MapShape) s).getKey().getTarget(), result, seen);
        collect(((MapShape) s).getValue().getTarget(), result, seen);
      }
      default -> {}
    }
  }

  // ---- emission ----------------------------------------------------------

  private void writeEnum(StringBuilder sb, EnumShape e) {
    String name = ProtoNaming.typeName(e.getId().getName());
    sb.append("enum ").append(name).append(" {\n");
    // Proto3 requires a zero value; add _UNSPECIFIED unless one already exists.
    sb.append("  ").append(name).append("_UNSPECIFIED = 0;\n");
    int idx = 1;
    for (var entry : e.getEnumValues().entrySet()) {
      sb.append("  ")
          .append(name)
          .append("_")
          .append(entry.getKey().toUpperCase())
          .append(" = ")
          .append(idx++)
          .append(";\n");
    }
    sb.append("}\n\n");
  }

  private void writeIntEnum(StringBuilder sb, IntEnumShape e) {
    String name = ProtoNaming.typeName(e.getId().getName());
    sb.append("enum ").append(name).append(" {\n");
    // Only emit _UNSPECIFIED = 0 when no existing value already occupies 0.
    boolean hasZero = e.getEnumValues().values().stream().anyMatch(v -> v == 0);
    if (!hasZero) {
      sb.append("  ").append(name).append("_UNSPECIFIED = 0;\n");
    }
    for (var entry : e.getEnumValues().entrySet()) {
      sb.append("  ")
          .append(name)
          .append("_")
          .append(entry.getKey().toUpperCase())
          .append(" = ")
          .append(entry.getValue())
          .append(";\n");
    }
    sb.append("}\n\n");
  }

  private void writeMessage(StringBuilder sb, StructureShape s, String localNs) {
    List<MemberShape> members = new ArrayList<>(s.members());
    members.sort(Comparator.comparingInt(this::protoIndex));

    // Validate that all field numbers within this message are distinct.
    // Inlined oneof members share the parent message's field number space, so
    // their indices must be checked alongside the regular member indices.
    // The container member's protoIndex is reserved (not emitted) but still
    // must be unique to avoid gaps that could confuse readers.
    Set<Integer> usedIndices = new HashSet<>();
    for (MemberShape m : members) {
      Shape target = model.expectShape(m.getTarget());
      if (target.getType() == ShapeType.UNION && target.hasTrait(PROTO_INLINED_ONE_OF)) {
        addProtoIndex(s.getId(), m, usedIndices);
        for (MemberShape um : target.members()) {
          addProtoIndex(s.getId(), um, usedIndices);
        }
      } else {
        addProtoIndex(s.getId(), m, usedIndices);
      }
    }

    sb.append("message ").append(ProtoNaming.typeName(s.getId().getName())).append(" {\n");
    for (MemberShape m : members) {
      Shape target = model.expectShape(m.getTarget());
      if (target.getType() == ShapeType.UNION && target.hasTrait(PROTO_INLINED_ONE_OF)) {
        writeInlinedOneof(sb, m, (UnionShape) target, localNs);
      } else {
        writeField(sb, m, false, localNs);
      }
    }
    sb.append("}\n\n");
  }

  private void writeUnionMessage(StringBuilder sb, UnionShape u, String localNs) {
    String name = ProtoNaming.typeName(u.getId().getName());
    List<MemberShape> members = new ArrayList<>(u.members());
    members.sort(Comparator.comparingInt(this::protoIndex));
    validateProtoIndices(u.getId(), members);

    sb.append("message ").append(name).append(" {\n");
    sb.append("  oneof value {\n");
    for (MemberShape m : members) {
      sb.append("    ")
          .append(fieldType(m, true, localNs))
          .append(" ")
          .append(camelToSnake(m.getMemberName()))
          .append(" = ")
          .append(protoIndex(m))
          .append(";\n");
    }
    sb.append("  }\n");
    sb.append("}\n\n");
  }

  /**
   * Writes an inlined oneof block for a member whose target union has {@code @protoInlinedOneOf}.
   * The union's members are emitted directly inside the parent message's oneof block named after
   * the parent member.
   */
  private void writeInlinedOneof(
      StringBuilder sb, MemberShape parentMember, UnionShape u, String localNs) {
    List<MemberShape> members = new ArrayList<>(u.members());
    members.sort(Comparator.comparingInt(this::protoIndex));

    sb.append("  oneof ").append(camelToSnake(parentMember.getMemberName())).append(" {\n");
    for (MemberShape m : members) {
      sb.append("    ")
          .append(fieldType(m, true, localNs))
          .append(" ")
          .append(camelToSnake(m.getMemberName()))
          .append(" = ")
          .append(protoIndex(m))
          .append(";\n");
    }
    sb.append("  }\n");
  }

  private void writeField(StringBuilder sb, MemberShape m, boolean inOneof, String localNs) {
    Shape target = model.expectShape(m.getTarget());
    String label = "";
    if (target.getType() == ShapeType.LIST || target.getType() == ShapeType.SET) {
      label = "repeated ";
    } else if (target.getType() != ShapeType.MAP && !inOneof && isProtoOptional(m)) {
      label = "optional ";
    }
    sb.append("  ")
        .append(label)
        .append(fieldType(m, false, localNs))
        .append(" ")
        .append(camelToSnake(m.getMemberName()))
        .append(" = ")
        .append(protoIndex(m))
        .append(";\n");
  }

  // ---- type resolution ---------------------------------------------------

  /**
   * Returns the proto3 type string for a member's target, taking list/map wrappers into account.
   * For direct (non-collection) members, the member shape is used so that {@code @protoNumType} is
   * respected.
   */
  private String fieldType(MemberShape m, boolean inOneof, String localNs) {
    Shape target = model.expectShape(m.getTarget());
    return switch (target.getType()) {
      case LIST, SET -> scalarOrRef(((ListShape) target).getMember(), localNs);
      case MAP -> {
        MapShape map = (MapShape) target;
        validateMapKey(map);
        // @sparse maps use google.protobuf.Value to allow null values.
        String valueType =
            map.hasTrait(SparseTrait.class)
                ? "google.protobuf.Value"
                : scalarOrRef(map.getValue(), localNs);
        yield "map<" + scalarOrRef(map.getKey(), localNs) + ", " + valueType + ">";
      }
      // Pass the member for @protoNumType resolution.
      default -> scalarOrRef(m, localNs);
    };
  }

  /**
   * Resolves the proto3 scalar/reference for a member, respecting {@code @protoNumType} for integer
   * types.
   */
  private String scalarOrRef(MemberShape m, String localNs) {
    Shape target = model.expectShape(m.getTarget());
    Optional<String> numType =
        m.findTrait(PROTO_NUM_TYPE).map(t -> t.toNode().expectStringNode().getValue());
    if (numType.isPresent()) {
      boolean isLong = target.getType() == ShapeType.LONG;
      return switch (numType.get()) {
        case "SIGNED" -> isLong ? "sint64" : "sint32";
        case "UNSIGNED" -> isLong ? "uint64" : "uint32";
        case "FIXED" -> isLong ? "fixed64" : "fixed32";
        case "FIXED_SIGNED" -> isLong ? "sfixed64" : "sfixed32";
        default -> scalarOrRef(target, localNs);
      };
    }
    return scalarOrRef(target, localNs);
  }

  private String scalarOrRef(Shape s, String localNs) {
    return switch (s.getType()) {
      case BOOLEAN -> "bool";
      case BYTE, SHORT, INTEGER -> "int32";
      case LONG -> "int64";
      case FLOAT -> "float";
      case DOUBLE -> "double";
      case STRING -> "string";
      case BLOB -> "bytes";
      case TIMESTAMP -> "google.protobuf.Timestamp";
      case DOCUMENT -> "google.protobuf.Value";
      case BIG_INTEGER, BIG_DECIMAL -> "string";
      default -> {
        // smithy.api#Unit has no proto equivalent — map to google.protobuf.Empty.
        if (UNIT.equals(s.getId())) yield "google.protobuf.Empty";
        String ns = s.getId().getNamespace();
        String typeName = ProtoNaming.typeName(s.getId().getName());
        // Qualify with proto package when the shape is from a foreign namespace.
        yield ns.equals(localNs) ? typeName : ns + "." + typeName;
      }
    };
  }

  /** Proto reference for an operation's input/output shape (handles Unit). */
  private String messageRef(ShapeId id, String localNs) {
    if (isUnit(id)) return "google.protobuf.Empty";
    Shape s = model.expectShape(id);
    String ns = s.getId().getNamespace();
    String name = ProtoNaming.typeName(id.getName());
    return ns.equals(localNs) ? name : ns + "." + name;
  }

  // ---- streaming detection -----------------------------------------------

  /**
   * Returns true if an operation's I/O shape is a streaming shape — i.e., if the shape itself or
   * any of its members (or their targets) carries {@code @streaming}. Used to emit the {@code
   * stream} keyword in rpc signatures.
   */
  private boolean isStreamingShape(ShapeId shapeId) {
    if (isUnit(shapeId)) return false;
    Shape s = model.expectShape(shapeId);
    if (s.hasTrait(StreamingTrait.class)) return true;
    for (MemberShape m : s.members()) {
      if (m.hasTrait(StreamingTrait.class)) return true;
      if (model.expectShape(m.getTarget()).hasTrait(StreamingTrait.class)) return true;
    }
    return false;
  }

  private Optional<ShapeId> streamingMemberTarget(ShapeId shapeId) {
    if (isUnit(shapeId)) return Optional.empty();
    Shape s = model.expectShape(shapeId);
    if (s.hasTrait(StreamingTrait.class)) return Optional.of(shapeId);
    return s.members().stream()
        .filter(
            m ->
                m.hasTrait(StreamingTrait.class)
                    || model.expectShape(m.getTarget()).hasTrait(StreamingTrait.class))
        .map(MemberShape::getTarget)
        .findFirst();
  }

  // ---- optional / required -----------------------------------------------

  private boolean isProtoOptional(MemberShape m) {
    if (m.hasTrait(RequiredTrait.class)) return false;
    if (m.hasTrait(DefaultTrait.class)) return false;
    Shape target = model.expectShape(m.getTarget());
    return switch (target.getType()) {
      case STRING,
          BLOB,
          BOOLEAN,
          BYTE,
          SHORT,
          INTEGER,
          LONG,
          FLOAT,
          DOUBLE,
          TIMESTAMP,
          DOCUMENT,
          ENUM,
          INT_ENUM ->
          true;
      default -> false;
    };
  }

  // ---- validation --------------------------------------------------------

  /**
   * Validates that no two members in {@code members} share the same {@code @protoIndex} field
   * number.
   */
  private void validateProtoIndices(ShapeId parentId, List<MemberShape> members) {
    Set<Integer> seen = new HashSet<>();
    for (MemberShape m : members) {
      addProtoIndex(parentId, m, seen);
    }
  }

  private void addProtoIndex(ShapeId parentId, MemberShape m, Set<Integer> seen) {
    int idx = protoIndex(m);
    if (!seen.add(idx)) {
      throw new CodegenException(
          "Duplicate @protoIndex "
              + idx
              + " in "
              + parentId
              + " (member: "
              + m.getMemberName()
              + ")");
    }
  }

  /**
   * Validates that the map key type is legal in proto3. Float, double, bytes, message types,
   * timestamp, and document are not allowed as map keys.
   */
  private void validateMapKey(MapShape map) {
    Shape keyShape = model.expectShape(map.getKey().getTarget());
    boolean invalid =
        switch (keyShape.getType()) {
          case FLOAT, DOUBLE, BLOB, STRUCTURE, UNION, TIMESTAMP, DOCUMENT -> true;
          default -> false;
        };
    if (invalid) {
      throw new CodegenException(
          "Proto3 map keys cannot be of type " + keyShape.getType() + ": " + map.getId());
    }
  }

  // ---- import detection helpers ------------------------------------------

  private boolean shapesNeedTimestamp(List<ShapeId> shapes) {
    return shapes.stream().anyMatch(id -> shapeUsesType(id, ShapeType.TIMESTAMP));
  }

  private boolean shapesNeedValue(List<ShapeId> shapes) {
    return shapes.stream()
        .anyMatch(id -> shapeUsesType(id, ShapeType.DOCUMENT) || shapeUsesSparseMap(id));
  }

  private boolean shapeUsesType(ShapeId id, ShapeType type) {
    Shape s = model.expectShape(id);
    for (MemberShape m : s.members()) {
      Shape target = model.expectShape(m.getTarget());
      if (target.getType() == type) return true;
      // Check element type for lists/sets.
      if ((target.getType() == ShapeType.LIST || target.getType() == ShapeType.SET)
          && model.expectShape(((ListShape) target).getMember().getTarget()).getType() == type) {
        return true;
      }
      // Check value type for maps.
      if (target.getType() == ShapeType.MAP
          && model.expectShape(((MapShape) target).getValue().getTarget()).getType() == type) {
        return true;
      }
    }
    return false;
  }

  /** Returns true if the shape has any map member with {@code @sparse}. */
  private boolean shapeUsesSparseMap(ShapeId id) {
    Shape s = model.expectShape(id);
    for (MemberShape m : s.members()) {
      Shape target = model.expectShape(m.getTarget());
      if (target.getType() == ShapeType.MAP && target.hasTrait(SparseTrait.class)) return true;
    }
    return false;
  }

  /**
   * Checks if an operation's I/O shape (when it is a structure) directly uses a well-known type,
   * adding the required import to {@code imports}.
   */
  private void checkWellKnownImports(ShapeId id, Set<String> imports, String localNs) {
    if (isUnit(id)) return;
    Shape s = model.expectShape(id);
    for (MemberShape m : s.members()) {
      Shape target = model.expectShape(m.getTarget());
      ShapeType t = target.getType();
      if (t == ShapeType.TIMESTAMP) imports.add(IMPORT_TIMESTAMP);
      if (t == ShapeType.DOCUMENT) imports.add(IMPORT_VALUE);
      if (t == ShapeType.MAP && target.hasTrait(SparseTrait.class)) imports.add(IMPORT_VALUE);
    }
  }

  // ---- proto index -------------------------------------------------------

  private int protoIndex(MemberShape m) {
    return m.findTrait(PROTO_INDEX)
        .map(t -> ((Node) t.toNode()).expectNumberNode().getValue().intValue())
        .orElseThrow(
            () ->
                new CodegenException(
                    "Member " + m.getId() + " is missing required @protoIndex trait."));
  }

  // ---- utilities ---------------------------------------------------------

  private static boolean isUnit(ShapeId id) {
    return UNIT.equals(id);
  }

  private static String camelToSnake(String name) {
    StringBuilder sb = new StringBuilder();
    for (int i = 0; i < name.length(); i++) {
      char c = name.charAt(i);
      if (i > 0
          && Character.isUpperCase(c)
          && (Character.isLowerCase(name.charAt(i - 1))
              || (i + 1 < name.length() && Character.isLowerCase(name.charAt(i + 1))))) {
        sb.append('_');
      }
      sb.append(Character.toLowerCase(c));
    }
    return sb.toString();
  }
}
