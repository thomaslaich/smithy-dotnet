package io.github.thomaslaich.nsmithy.fsharp.codegen;

import io.github.thomaslaich.nsmithy.codegen.core.ClientNullability;
import software.amazon.smithy.model.Model;

/**
 * Applies {@link ClientNullability} to the shapes the project generates. It preprocesses the model
 * after every other integration, once the operations have dedicated {@code @input} structures.
 */
final class ClientNullabilityIntegration implements FSharpIntegration {

  @Override
  public String name() {
    return "nsmithy-client-nullability";
  }

  @Override
  public byte priority() {
    return Byte.MIN_VALUE;
  }

  @Override
  public Model preprocessModel(Model model, FSharpSettings settings) {
    return ClientNullability.apply(model, id -> !settings.providedShapes().contains(id));
  }
}
