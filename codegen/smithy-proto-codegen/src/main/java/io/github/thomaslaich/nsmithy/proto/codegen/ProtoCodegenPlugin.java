package io.github.thomaslaich.nsmithy.proto.codegen;

import software.amazon.smithy.build.PluginContext;
import software.amazon.smithy.build.SmithyBuildPlugin;
import software.amazon.smithy.model.node.Node;
import software.amazon.smithy.model.node.ObjectNode;
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
 *     "baseNamespace": "My.Base",
 *     "fileOptions": {
 *       "csharp_namespace": {
 *         "suffix": "Grpc",
 *         "case": "pascal"
 *       },
 *       "java_package": {
 *         "prefix": "com.myorg",
 *         "suffix": "grpc",
 *         "case": "lower"
 *       },
 *       "java_multiple_files": true
 *     }
 *   }
 * }
 * }</pre>
 *
 * <p>{@code fileOptions} is optional. String, boolean, and number values are emitted as protobuf
 * file options. Object values derive a namespace-like option value from the Smithy namespace using
 * optional {@code prefix}, {@code suffix}, and {@code case} members. When a file option object
 * omits {@code prefix}, the top-level {@code baseNamespace} is used.
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

    ObjectNode fileOptions = settingsFileOptions(settings);

    new ProtoGenerator(
            context.getModel(), service, baseNamespace, fileOptions, context.getFileManifest())
        .run();
  }

  private static ObjectNode settingsFileOptions(ObjectNode settings) {
    return settings
        .getMember("fileOptions")
        .flatMap(Node::asObjectNode)
        .orElseGet(Node::objectNode);
  }
}
