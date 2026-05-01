/*
 * Emits the [SmithyShape] / [SmithyMember] / [SmithyTrait] reflection
 * attributes that NSmithy.Core relies on for runtime introspection. Mirrors
 * the AddShapeAttributes / AddMemberAttributes / AddTraitAttributes helpers
 * in CSharpShapeGenerator.TypeEmitter.cs.
 */
package io.github.thomaslaich.smithy.csharp.codegen.support;

import io.github.thomaslaich.smithy.csharp.codegen.CSharpNaming;
import io.github.thomaslaich.smithy.csharp.codegen.RuntimeTypes;
import io.github.thomaslaich.smithy.csharp.codegen.writer.CSharpWriter;
import java.util.ArrayList;
import java.util.Comparator;
import java.util.List;
import java.util.Optional;
import software.amazon.smithy.model.node.Node;
import software.amazon.smithy.model.shapes.MemberShape;
import software.amazon.smithy.model.shapes.Shape;
import software.amazon.smithy.model.shapes.ShapeType;
import software.amazon.smithy.model.traits.JsonNameTrait;
import software.amazon.smithy.model.traits.Trait;
import software.amazon.smithy.utils.SmithyInternalApi;

@SmithyInternalApi
public final class AttributeEmitter {

  private AttributeEmitter() {}

  public static void writeShapeAttributes(CSharpWriter w, Shape shape) {
    w.addImport(RuntimeTypes.NSMITHY_CORE);
    w.addImport(RuntimeTypes.NSMITHY_CORE_ANNOTATIONS);
    w.write(
        "[SmithyShape($L, ShapeKind.$L)]",
        CSharpNaming.formatString(shape.getId().toString()),
        shapeKindName(shape.getType()));
    writeTraitAttributes(w, shape);
  }

  public static void writeMemberAttributes(CSharpWriter w, MemberShape m, boolean sparse) {
    w.addImport(RuntimeTypes.NSMITHY_CORE_ANNOTATIONS);
    List<String> args = new ArrayList<>();
    args.add(CSharpNaming.formatString(m.getMemberName()));
    args.add(CSharpNaming.formatString(m.getTarget().toString()));
    if (m.hasTrait(software.amazon.smithy.model.traits.RequiredTrait.class)) {
      args.add("IsRequired = true");
    }
    if (sparse) {
      args.add("IsSparse = true");
    }
    m.getTrait(JsonNameTrait.class)
        .ifPresent(jn -> args.add("JsonName = " + CSharpNaming.formatString(jn.getValue())));
    w.write("[SmithyMember($L)]", String.join(", ", args));
    writeTraitAttributes(w, m);
  }

  private static void writeTraitAttributes(CSharpWriter w, Shape shape) {
    List<Trait> traits = new ArrayList<>(shape.getAllTraits().values());
    traits.sort(Comparator.comparing(t -> t.toShapeId().toString()));
    for (Trait t : traits) {
      String idStr = CSharpNaming.formatString(t.toShapeId().toString());
      Optional<String> v = traitValueLiteral(t);
      if (v.isPresent()) {
        w.write("[SmithyTrait($L, Value = $L)]", idStr, CSharpNaming.formatString(v.get()));
      } else {
        w.write("[SmithyTrait($L)]", idStr);
      }
    }
  }

  private static Optional<String> traitValueLiteral(Trait t) {
    Node n = t.toNode();
    return switch (n.getType()) {
      case BOOLEAN -> Optional.of(Boolean.toString(n.expectBooleanNode().getValue()));
      case NUMBER -> Optional.of(n.expectNumberNode().getValue().toString());
      case STRING -> Optional.of(n.expectStringNode().getValue());
      default -> Optional.empty();
    };
  }

  public static String shapeKindName(ShapeType t) {
    // ShapeType.STRUCTURE -> "Structure". Special-case enum/intEnum.
    return switch (t) {
      case INT_ENUM -> "IntEnum";
      default -> {
        String name = t.name().toLowerCase();
        yield Character.toUpperCase(name.charAt(0)) + name.substring(1);
      }
    };
  }
}
