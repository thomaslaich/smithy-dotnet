package io.github.thomaslaich.nsmithy.codegen.core;

import java.util.List;
import java.util.Optional;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.shapes.ShapeId;
import software.amazon.smithy.model.transform.ModelTransformer;
import software.amazon.smithy.utils.SmithyInternalApi;

/**
 * The model a generator runs on, prepared from the model as assembled. Everything after it, the
 * owned shapes, the shape closure, the provided-shape checks, and code generation, sees this model.
 */
@SmithyInternalApi
public final class CodegenModel {

  private CodegenModel() {}

  /**
   * The assembled model with mixins flattened, {@code smithy.framework#ValidationException}
   * declared or checked (see {@link ValidationErrors#apply}), a dedicated {@code @input} and
   * {@code @output} structure for every operation, and, for a project that follows them, the client
   * rules of {@link ClientNullability} applied to the shapes it generates.
   *
   * <p>Not performDefaultCodegenTransforms: it also copies a service's errors onto its operations,
   * but operation schemas are service-agnostic. The service schema carries those errors.
   */
  public static Model prepare(
      Model source,
      Optional<ShapeId> service,
      boolean attachValidationException,
      boolean generateClient,
      boolean generateServer,
      List<String> ownedSources,
      ProvidedShapes provided) {
    ModelTransformer transformer = ModelTransformer.create();
    Model model =
        ValidationErrors.apply(
            transformer.flattenAndRemoveMixins(source),
            service,
            attachValidationException,
            generateServer);
    model = transformer.createDedicatedInputAndOutput(model, "Input", "Output");
    if (ClientNullability.applies(
        model, OwnedShapes.of(model, ownedSources), generateClient, generateServer)) {
      model = ClientNullability.apply(model, id -> !provided.contains(id));
    }
    return model;
  }
}
