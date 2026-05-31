package io.github.thomaslaich.nsmithy.proto.codegen;

import software.amazon.smithy.build.PluginContext;
import software.amazon.smithy.build.SmithyBuildPlugin;
import software.amazon.smithy.model.shapes.ServiceShape;
import software.amazon.smithy.model.shapes.ShapeId;
import software.amazon.smithy.utils.SmithyUnstableApi;

/**
 * Smithy build plugin that emits proto3 {@code .proto} files for services annotated with {@code
 * alloy.proto#grpc}.
 *
 * <p>Plugin name: {@code proto-codegen}. Add to {@code smithy-build.json}:
 *
 * <pre>{@code
 * "plugins": {
 *   "proto-codegen": {
 *     "service": "my.namespace#MyService",
 *     "baseNamespace": "My.Base"
 *   }
 * }
 * }</pre>
 *
 * <p>{@code baseNamespace} is optional. When set, the generated {@code option csharp_namespace}
 * will be prefixed with it, matching what {@code smithy-csharp-codegen} produces.
 */
@SmithyUnstableApi
public final class ProtoCodegenPlugin implements SmithyBuildPlugin {

  @Override
  public String getName() {
    return "proto-codegen";
  }

  @Override
  public void execute(PluginContext context) {
    var settings = context.getSettings();

    ShapeId serviceId =
        ShapeId.from(
            settings
                .getStringMember("service")
                .map(s -> s.getValue())
                .orElseThrow(
                    () ->
                        new IllegalArgumentException(
                            "proto-codegen: 'service' setting is required")));

    String baseNamespace =
        settings.getStringMember("baseNamespace").map(s -> s.getValue()).orElse("");

    ServiceShape service = context.getModel().expectShape(serviceId, ServiceShape.class);

    String csharpNamespace =
        ProtoNaming.namespaceFor(service.getId().getNamespace(), baseNamespace);

    new ProtoGenerator(
            context.getModel(), service, csharpNamespace, baseNamespace, context.getFileManifest())
        .run();
  }
}
