package io.github.thomaslaich.nsmithy.codegen.core;

import java.util.ArrayList;
import java.util.List;
import java.util.Set;
import java.util.function.Predicate;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.shapes.MemberShape;
import software.amazon.smithy.model.shapes.Shape;
import software.amazon.smithy.model.shapes.ShapeId;
import software.amazon.smithy.model.shapes.StructureShape;
import software.amazon.smithy.model.traits.InputTrait;
import software.amazon.smithy.model.traits.RequiredTrait;
import software.amazon.smithy.model.transform.ModelTransformer;
import software.amazon.smithy.utils.SmithyInternalApi;

/**
 * Smithy's client rules for <a
 * href="https://smithy.io/2.0/spec/aggregate-types.html#structure-member-optionality">structure
 * member optionality</a>.
 *
 * <p>A client must treat every member of an input structure as nullable, and every member marked
 * {@code @clientOptional} as if it were not required and had no default. A server may ignore both
 * rules. One set of generated types serves every side a project generates, so the types follow the
 * client rules only when the project generates a client, no server, and no handler interface;
 * otherwise handlers get the exact types the server rules give. Shapes another project generated
 * keep the types their owner gave them.
 */
@SmithyInternalApi
public final class ClientNullability {

  private ClientNullability() {}

  /**
   * Whether a project follows the client rules: it generates a client and no server, and owns no
   * operation. The owner of an operation always generates its handler interface, for a server in
   * another project, so it is a contract and keeps the server rules.
   */
  public static boolean applies(
      Model model, Set<ShapeId> owned, boolean generateClient, boolean generateServer) {
    return generateClient
        && !generateServer
        && owned.stream()
            .noneMatch(id -> model.getShape(id).filter(Shape::isOperationShape).isPresent());
  }

  /**
   * The model with the client rules applied to the structures {@code generated} accepts: the
   * members of input structures and the members marked {@code @clientOptional} lose the required
   * trait. The generators already give a member with a default a nullable type and ignore the
   * default of a {@code @clientOptional} member, so a client still sends an input member's default
   * when the member is null. Run it after the operations get dedicated input structures.
   */
  public static Model apply(Model model, Predicate<ShapeId> generated) {
    List<Shape> updated = new ArrayList<>();
    for (StructureShape structure : model.getStructureShapes()) {
      if (!generated.test(structure.getId())
          || ValidationErrors.isFrameworkShape(structure.getId())) {
        continue;
      }
      boolean input = structure.hasTrait(InputTrait.class);
      StructureShape.Builder builder = null;
      for (MemberShape member : structure.members()) {
        if (!ShapeSupport.isRequired(member)
            || (!input && !ShapeSupport.hasClientOptional(member))) {
          continue;
        }
        if (builder == null) {
          builder = structure.toBuilder();
        }
        builder.addMember(member.toBuilder().removeTrait(RequiredTrait.ID).build());
      }
      if (builder != null) {
        updated.add(builder.build());
      }
    }
    return updated.isEmpty() ? model : ModelTransformer.create().replaceShapes(model, updated);
  }
}
