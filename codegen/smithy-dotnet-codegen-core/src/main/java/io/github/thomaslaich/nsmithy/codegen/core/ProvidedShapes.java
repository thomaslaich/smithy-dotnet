package io.github.thomaslaich.nsmithy.codegen.core;

import java.io.IOException;
import java.io.UncheckedIOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.Collections;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Optional;
import java.util.Set;
import software.amazon.smithy.codegen.core.CodegenException;
import software.amazon.smithy.codegen.core.Symbol;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.knowledge.TopDownIndex;
import software.amazon.smithy.model.node.Node;
import software.amazon.smithy.model.node.ObjectNode;
import software.amazon.smithy.model.node.StringNode;
import software.amazon.smithy.model.shapes.OperationShape;
import software.amazon.smithy.model.shapes.ServiceShape;
import software.amazon.smithy.model.shapes.ShapeId;
import software.amazon.smithy.model.traits.ErrorTrait;
import software.amazon.smithy.utils.SmithyInternalApi;

/**
 * The shapes other projects generated, read from their shape manifests ({@value
 * ShapeManifest#FILE_NAME}). A provided shape is not generated again: generated code refers to the
 * symbols its manifest names. Only manifests of the running generator provide shapes. C# and F#
 * projects do not share generated types, so the other generator's manifests are kept only to reject
 * generating a shape they list. See designs/shape-ownership.md.
 */
@SmithyInternalApi
public final class ProvidedShapes {

  /** No provided shapes. */
  public static final ProvidedShapes NONE = new ProvidedShapes(Map.of(), Map.of());

  private final Map<ShapeId, Entry> shapes;
  private final Map<ShapeId, String> foreign;

  private ProvidedShapes(Map<ShapeId, Entry> shapes, Map<ShapeId, String> foreign) {
    this.shapes = shapes;
    this.foreign = foreign;
  }

  /** A provided shape: the manifest that lists it and the symbols it names, by role. */
  private record Entry(String manifest, Map<String, String> symbols) {}

  /**
   * Reads the manifests at {@code paths} for {@code generator}. Fails when a manifest cannot be
   * read, has another format version, was written by another version of {@code generator}, or lists
   * a shape another of its manifests lists too.
   */
  public static ProvidedShapes load(List<String> paths, String generator) {
    if (paths.isEmpty()) {
      return NONE;
    }
    Map<ShapeId, Entry> shapes = new LinkedHashMap<>();
    Map<ShapeId, String> foreign = new LinkedHashMap<>();
    for (String path : paths) {
      ObjectNode manifest = read(path);
      boolean own = manifest.expectStringMember("generator").getValue().equals(generator);
      if (own) {
        checkVersion(manifest, path, generator);
      }
      for (Map.Entry<StringNode, Node> shape :
          manifest.expectObjectMember("shapes").getMembers().entrySet()) {
        ShapeId id = ShapeId.from(shape.getKey().getValue());
        if (!own) {
          foreign.putIfAbsent(id, path);
          continue;
        }
        Map<String, String> symbols = new LinkedHashMap<>();
        shape
            .getValue()
            .expectObjectNode()
            .getMembers()
            .forEach(
                (role, name) -> symbols.put(role.getValue(), name.expectStringNode().getValue()));
        Entry previous = shapes.putIfAbsent(id, new Entry(path, symbols));
        if (previous != null) {
          throw new CodegenException(
              "Shape "
                  + id
                  + " is provided by two shape manifests, "
                  + previous.manifest()
                  + " and "
                  + path
                  + ". Exactly one project may generate a shape; give it a project that owns it.");
        }
      }
    }
    return new ProvidedShapes(
        Collections.unmodifiableMap(shapes), Collections.unmodifiableMap(foreign));
  }

  private static ObjectNode read(String path) {
    String text;
    try {
      text = Files.readString(Path.of(path));
    } catch (IOException e) {
      throw new UncheckedIOException("Cannot read the shape manifest " + path, e);
    }
    ObjectNode manifest = Node.parse(text, path).expectObjectNode();
    int version = manifest.expectNumberMember("version").getValue().intValue();
    if (version != ShapeManifest.FORMAT_VERSION) {
      throw new CodegenException(
          "The shape manifest "
              + path
              + " has format version "
              + version
              + ", but this generator reads version "
              + ShapeManifest.FORMAT_VERSION
              + ".");
    }
    return manifest;
  }

  private static void checkVersion(ObjectNode manifest, String path, String generator) {
    String generatorVersion = manifest.expectStringMember("generatorVersion").getValue();
    String current = ShapeManifest.generatorVersion();
    if (!generatorVersion.equals(current)) {
      throw new CodegenException(
          "The shape manifest "
              + path
              + " was written by "
              + generator
              + " "
              + generatorVersion
              + ", but this build runs "
              + current
              + ". Generated code from different versions may not fit together; use the same"
              + " NSmithy version in every project.");
    }
  }

  /** The ids of all provided shapes. */
  public Set<ShapeId> ids() {
    return shapes.keySet();
  }

  /** Whether another project generated {@code id}. */
  public boolean contains(ShapeId id) {
    return shapes.containsKey(id);
  }

  /** The manifest that provides {@code id}. */
  public Optional<String> manifest(ShapeId id) {
    return Optional.ofNullable(shapes.get(id)).map(Entry::manifest);
  }

  /** The type {@code id} was generated as. */
  public Optional<Symbol> type(ShapeId id) {
    return symbol(id, ShapeManifest.TYPE);
  }

  /** The class holding {@code id}'s schema. */
  public Optional<Symbol> schema(ShapeId id) {
    return symbol(id, ShapeManifest.SCHEMA);
  }

  /** The handler interface of operation {@code id}. */
  public Optional<Symbol> handler(ShapeId id) {
    return symbol(id, ShapeManifest.HANDLER);
  }

  /**
   * Fails when a manifest of the other generator lists {@code id}: generating it would declare a
   * type that a referenced assembly already declares, since C# and F# projects do not share
   * generated types.
   */
  public void checkNotForeign(ShapeId id) {
    String manifest = foreign.get(id);
    if (manifest != null) {
      throw new CodegenException(
          "Shape "
              + id
              + " would be generated here, but the shape manifest "
              + manifest
              + ", written by the other .NET language's generator, lists it too. C# and F#"
              + " projects do not share generated types, so this project would declare a type that"
              + " a referenced assembly already declares. Set SmithyGenerateCode to false in the"
              + " project that wrote that manifest, so that it publishes its model only.");
    }
  }

  /**
   * Fails when the provided shapes cannot be used as they are: a shape this project defines itself
   * ({@code owned}), a service rename of a provided error, or, when the project generates the
   * server, a provided operation of the service without the handler interface the server needs.
   */
  public void check(
      Model model, Optional<ServiceShape> service, Set<ShapeId> owned, boolean generateServer) {
    for (ShapeId id : owned) {
      if (contains(id)) {
        throw new CodegenException(
            "Shape "
                + id
                + " is defined in this project's own model files, but the shape manifest "
                + manifest(id).orElseThrow()
                + " provides it too. A project's own manifest must not be passed to it.");
      }
    }
    if (service.isEmpty()) {
      return;
    }
    for (ShapeId id : service.get().getRename().keySet()) {
      if (contains(id) && model.expectShape(id).hasTrait(ErrorTrait.class)) {
        throw new CodegenException(
            "Service "
                + service.get().getId()
                + " renames the error "
                + id
                + ", which the shape manifest "
                + manifest(id).orElseThrow()
                + " provides. An error's name goes on the wire and NSmithy does not apply a rename"
                + " there, so clients that do would not recognize it. Give the error a distinct"
                + " name in its own model, or share one error from a common project.");
      }
    }
    if (generateServer) {
      for (OperationShape op : TopDownIndex.of(model).getContainedOperations(service.get())) {
        if (contains(op.getId()) && handler(op.getId()).isEmpty()) {
          throw new CodegenException(
              "Operation "
                  + op.getId()
                  + " of "
                  + service.get().getId()
                  + " is provided by the shape manifest "
                  + manifest(op.getId()).orElseThrow()
                  + " without a handler interface, because the project that generated it does not"
                  + " own it and has the server turned off. Generate the operation in a project"
                  + " that owns it, or turn the server off here.");
        }
      }
    }
  }

  private Optional<Symbol> symbol(ShapeId id, String role) {
    return Optional.ofNullable(shapes.get(id))
        .map(entry -> entry.symbols().get(role))
        .map(ProvidedShapes::fromFullName);
  }

  /** A symbol for a fully qualified type name. */
  public static Symbol fromFullName(String fullName) {
    int dot = fullName.lastIndexOf('.');
    return Symbol.builder()
        .name(fullName.substring(dot + 1))
        .namespace(dot < 0 ? "" : fullName.substring(0, dot), ".")
        .build();
  }
}
