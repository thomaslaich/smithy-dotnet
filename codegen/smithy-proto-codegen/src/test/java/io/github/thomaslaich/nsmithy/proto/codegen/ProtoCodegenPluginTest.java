package io.github.thomaslaich.nsmithy.proto.codegen;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import java.nio.file.Files;
import java.nio.file.Path;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;
import software.amazon.smithy.build.FileManifest;
import software.amazon.smithy.build.PluginContext;
import software.amazon.smithy.codegen.core.CodegenException;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.node.Node;
import software.amazon.smithy.model.node.ObjectNode;

final class ProtoCodegenPluginTest {

  private static final String ALLOY_PROTO_TRAITS =
      """
      $version: "2"

      namespace alloy.proto

      use smithy.api#trait

      @trait(selector: "service")
      structure grpc {}

      @trait(selector: "member")
      integer protoIndex

      @trait(selector: "union")
      structure protoInlinedOneOf {}
      """;

  private static final String MODEL =
      """
      $version: "2"

      namespace example.oneof

      use alloy.proto#grpc
      use alloy.proto#protoIndex
      use alloy.proto#protoInlinedOneOf

      @grpc
      service ExampleService {
          version: "1"
          operations: [Choose]
      }

      operation Choose {
          input: ChoiceInput
          output: ChoiceInput
      }

      structure ChoiceInput {
          @protoIndex(2)
          selector: Choice
      }

      @protoInlinedOneOf
      union Choice {
          @protoIndex(3)
          byName: String

          @protoIndex(4)
          byCount: Integer
      }
      """;

  private static final String STREAMING_MODEL =
      """
      $version: "2"

      namespace example.streaming

      use alloy.proto#grpc
      use alloy.proto#protoIndex

      @grpc
      service StreamingService {
          version: "1"
          operations: [Watch]
      }

      operation Watch {
          input: WatchInput
          output: WatchOutput
      }

      structure WatchInput {
          events: WatchInputEvent
      }

      @streaming
      union WatchInputEvent {
          @protoIndex(1)
          filter: WatchFilter
      }

      structure WatchFilter {
          @required
          @protoIndex(1)
          prefix: String
      }

      structure WatchOutput {
          events: WatchOutputEvent
      }

      @streaming
      union WatchOutputEvent {
          @protoIndex(1)
          reading: WatchReading
      }

      structure WatchReading {
          @required
          @protoIndex(1)
          name: String
      }
      """;

  private static final String COMMON_NAMESPACE_MODEL =
      """
      $version: "2"

      namespace example.common

      use alloy.proto#protoIndex

      structure SharedPayload {
          @protoIndex(1)
          name: String
      }
      """;

  private static final String CROSS_NAMESPACE_MODEL =
      """
      $version: "2"

      namespace example.cross

      use alloy.proto#grpc
      use alloy.proto#protoIndex
      use example.common#SharedPayload

      @grpc
      service CrossNamespaceService {
          version: "1"
          operations: [Share]
      }

      operation Share {
          input: ShareInput
          output: ShareOutput
      }

      structure ShareInput {
          @protoIndex(1)
          payload: SharedPayload
      }

      structure ShareOutput {
          @protoIndex(1)
          accepted: Boolean
      }
      """;

  private static final String WELL_KNOWN_TYPES_MODEL =
      """
      $version: "2"

      namespace example.wellknown

      use alloy.proto#grpc
      use alloy.proto#protoIndex

      @grpc
      service WellKnownService {
          version: "1"
          operations: [GetMetadata, Ping]
      }

      operation GetMetadata {
          input: MetadataInput
          output: MetadataOutput
      }

      operation Ping {
          input: PingInput
          output: Unit
      }

      structure MetadataInput {
          @protoIndex(1)
          at: Timestamp

          @protoIndex(2)
          payload: Document
      }

      structure MetadataOutput {
          @protoIndex(1)
          echoedAt: Timestamp

          @protoIndex(2)
          payload: Document
      }

      structure PingInput {
          @protoIndex(1)
          message: String
      }
      """;

  private static final String DUPLICATE_PROTO_INDEX_MODEL =
      """
      $version: "2"

      namespace example.invalid

      use alloy.proto#grpc
      use alloy.proto#protoIndex

      @grpc
      service InvalidService {
          version: "1"
          operations: [Broken]
      }

      operation Broken {
          input: BrokenInput
          output: BrokenInput
      }

      structure BrokenInput {
          @protoIndex(1)
          first: String

          @protoIndex(1)
          second: Integer
      }
      """;

  private static final String STREAMING_SIBLING_MODEL =
      """
      $version: "2"

      namespace example.invalidstream

      use alloy.proto#grpc
      use alloy.proto#protoIndex

      @grpc
      service InvalidStreamingService {
          version: "1"
          operations: [Chat]
      }

      operation Chat {
          input: ChatInput
          output: ChatOutput
      }

      structure ChatInput {
          @protoIndex(1)
          room: String

          @protoIndex(2)
          events: ChatEvent
      }

      structure ChatOutput {
          @protoIndex(1)
          events: ChatEvent
      }

      @streaming
      union ChatEvent {
          @protoIndex(1)
          message: MessageEvent
      }

      structure MessageEvent {
          @protoIndex(1)
          text: String
      }
      """;

  @TempDir Path tempDir;

  @Test
  void executeGeneratesExpectedProtoFileFromSmithyModel() throws Exception {
    Model model =
        Model.assembler()
            .addUnparsedModel("alloy-proto-traits.smithy", ALLOY_PROTO_TRAITS)
            .addUnparsedModel("model.smithy", MODEL)
            .assemble()
            .unwrap();
    FileManifest manifest = FileManifest.create(tempDir);

    new ProtoCodegenPlugin()
        .execute(
            PluginContext.builder()
                .model(model)
                .fileManifest(manifest)
                .settings(
                    ObjectNode.builder()
                        .withMember("service", Node.from("example.oneof#ExampleService"))
                        .withMember("baseNamespace", Node.from("My.Base"))
                        .withMember(
                            "fileOptions",
                            ObjectNode.builder()
                                .withMember(
                                    "csharp_namespace",
                                    ObjectNode.builder()
                                        .withMember("suffix", Node.from("Grpc"))
                                        .withMember("case", Node.from("pascal"))
                                        .build())
                                .build())
                        .build())
                .build());

    Path protoPath = Path.of("My/Base/Example/Oneof/ExampleService.proto");
    assertTrue(manifest.hasFile(protoPath));
    assertEquals(
        """
        // <auto-generated />
        // Generated by smithy-proto-codegen. DO NOT EDIT.
        syntax = "proto3";

        package example.oneof;

        option csharp_namespace = "My.Base.Example.Oneof.Grpc";

        service ExampleService {
          rpc Choose (ChoiceInput) returns (ChoiceInput);
        }

        message ChoiceInput {
          oneof selector {
            string by_name = 3;
            int32 by_count = 4;
          }
        }

        """,
        Files.readString(tempDir.resolve(protoPath)));
  }

  @Test
  void executeOmitsLanguageSpecificOptionsWhenFileOptionsAreOmitted() throws Exception {
    Model model =
        Model.assembler()
            .addUnparsedModel("alloy-proto-traits.smithy", ALLOY_PROTO_TRAITS)
            .addUnparsedModel("model.smithy", MODEL)
            .assemble()
            .unwrap();
    FileManifest manifest = FileManifest.create(tempDir);

    new ProtoCodegenPlugin()
        .execute(
            PluginContext.builder()
                .model(model)
                .fileManifest(manifest)
                .settings(
                    ObjectNode.builder()
                        .withMember("service", Node.from("example.oneof#ExampleService"))
                        .withMember("baseNamespace", Node.from("My.Base"))
                        .build())
                .build());

    String proto = Files.readString(tempDir.resolve("My/Base/Example/Oneof/ExampleService.proto"));
    assertFalse(proto.contains("option csharp_namespace"), proto);
  }

  @Test
  void executeInfersJavaPackageFromSmithyNamespace() throws Exception {
    Model model =
        Model.assembler()
            .addUnparsedModel("alloy-proto-traits.smithy", ALLOY_PROTO_TRAITS)
            .addUnparsedModel("model.smithy", MODEL)
            .assemble()
            .unwrap();
    FileManifest manifest = FileManifest.create(tempDir);

    new ProtoCodegenPlugin()
        .execute(
            PluginContext.builder()
                .model(model)
                .fileManifest(manifest)
                .settings(
                    ObjectNode.builder()
                        .withMember("service", Node.from("example.oneof#ExampleService"))
                        .withMember(
                            "fileOptions",
                            ObjectNode.builder()
                                .withMember(
                                    "java_package",
                                    ObjectNode.builder()
                                        .withMember("prefix", Node.from("com.myorg"))
                                        .withMember("suffix", Node.from("grpc"))
                                        .withMember("case", Node.from("lower"))
                                        .build())
                                .withMember("java_multiple_files", Node.from(true))
                                .build())
                        .build())
                .build());

    String proto = Files.readString(tempDir.resolve("Example/Oneof/ExampleService.proto"));
    assertTrue(proto.contains("option java_package = \"com.myorg.example.oneof.grpc\";"), proto);
    assertTrue(proto.contains("option java_multiple_files = true;"), proto);
  }

  @Test
  void executeUsesStreamingUnionTargetsForGrpcStreams() throws Exception {
    Model model =
        Model.assembler()
            .addUnparsedModel("alloy-proto-traits.smithy", ALLOY_PROTO_TRAITS)
            .addUnparsedModel("model.smithy", STREAMING_MODEL)
            .assemble()
            .unwrap();
    FileManifest manifest = FileManifest.create(tempDir);

    new ProtoCodegenPlugin()
        .execute(
            PluginContext.builder()
                .model(model)
                .fileManifest(manifest)
                .settings(
                    ObjectNode.builder()
                        .withMember("service", Node.from("example.streaming#StreamingService"))
                        .build())
                .build());

    String proto = Files.readString(tempDir.resolve("Example/Streaming/StreamingService.proto"));
    assertTrue(
        proto.contains("  rpc Watch (stream WatchInputEvent) returns (stream WatchOutputEvent);"),
        proto);
    assertFalse(proto.contains("message WatchInput {"), proto);
    assertFalse(proto.contains("message WatchOutput {"), proto);
  }

  @Test
  void executeFailsWhenStreamingWrapperHasSiblingMembers() {
    Model model =
        Model.assembler()
            .addUnparsedModel("alloy-proto-traits.smithy", ALLOY_PROTO_TRAITS)
            .addUnparsedModel("model.smithy", STREAMING_SIBLING_MODEL)
            .assemble()
            .unwrap();
    FileManifest manifest = FileManifest.create(tempDir);

    CodegenException ex =
        assertThrows(
            CodegenException.class,
            () ->
                new ProtoCodegenPlugin()
                    .execute(
                        PluginContext.builder()
                            .model(model)
                            .fileManifest(manifest)
                            .settings(
                                ObjectNode.builder()
                                    .withMember(
                                        "service",
                                        Node.from("example.invalidstream#InvalidStreamingService"))
                                    .build())
                            .build()));

    assertTrue(
        ex.getMessage()
            .contains(
                "gRPC event-stream operation example.invalidstream#Chat input shape"
                    + " example.invalidstream#ChatInput must contain exactly one streaming member"),
        ex.getMessage());
  }

  @Test
  void executeGeneratesSeparateTypesFileForForeignNamespace() throws Exception {
    Model model =
        Model.assembler()
            .addUnparsedModel("alloy-proto-traits.smithy", ALLOY_PROTO_TRAITS)
            .addUnparsedModel("common.smithy", COMMON_NAMESPACE_MODEL)
            .addUnparsedModel("model.smithy", CROSS_NAMESPACE_MODEL)
            .assemble()
            .unwrap();
    FileManifest manifest = FileManifest.create(tempDir);

    new ProtoCodegenPlugin()
        .execute(
            PluginContext.builder()
                .model(model)
                .fileManifest(manifest)
                .settings(
                    ObjectNode.builder()
                        .withMember("service", Node.from("example.cross#CrossNamespaceService"))
                        .build())
                .build());

    Path serviceProto = Path.of("Example/Cross/CrossNamespaceService.proto");
    Path foreignTypesProto = Path.of("Example/Common/types.proto");
    assertTrue(manifest.hasFile(serviceProto));
    assertTrue(manifest.hasFile(foreignTypesProto));

    String service = Files.readString(tempDir.resolve(serviceProto));
    assertTrue(service.contains("import \"Example/Common/types.proto\";"), service);
    assertTrue(service.contains("example.common.SharedPayload payload = 1;"), service);

    String foreignTypes = Files.readString(tempDir.resolve(foreignTypesProto));
    assertTrue(foreignTypes.contains("package example.common;"), foreignTypes);
    assertTrue(foreignTypes.contains("message SharedPayload {"), foreignTypes);
  }

  @Test
  void importsTheNamespacesAForeignInlinedOneofNames() throws Exception {
    Model model =
        Model.assembler()
            .addUnparsedModel("alloy-proto-traits.smithy", ALLOY_PROTO_TRAITS)
            .addUnparsedModel(
                "choices.smithy",
                """
                $version: "2"
                namespace example.choices
                use alloy.proto#protoIndex
                use alloy.proto#protoInlinedOneOf

                @protoInlinedOneOf
                union Choice {
                    @protoIndex(2)
                    thing: example.things#Thing
                    @protoIndex(3)
                    label: String
                }
                """)
            .addUnparsedModel(
                "things.smithy",
                """
                $version: "2"
                namespace example.things
                use alloy.proto#protoIndex

                structure Thing {
                    @protoIndex(1)
                    name: String
                }
                """)
            .addUnparsedModel(
                "model.smithy",
                """
                $version: "2"
                namespace example.picker
                use alloy.proto#grpc
                use alloy.proto#protoIndex

                @grpc
                service Picker {
                    version: "1"
                    operations: [Pick]
                }

                operation Pick {
                    input := {
                        @protoIndex(1)
                        choice: example.choices#Choice
                    }
                }
                """)
            .assemble()
            .unwrap();

    new ProtoCodegenPlugin()
        .execute(
            PluginContext.builder()
                .model(model)
                .fileManifest(FileManifest.create(tempDir))
                .settings(
                    ObjectNode.builder()
                        .withMember("service", Node.from("example.picker#Picker"))
                        .build())
                .build());

    String service = Files.readString(tempDir.resolve("Example/Picker/Picker.proto"));
    assertTrue(service.contains("example.things.Thing thing = 2;"), service);
    assertTrue(service.contains("import \"Example/Things/types.proto\";"), service);
  }

  @Test
  void executeMapsWellKnownSmithyTypesToWellKnownProtoTypes() throws Exception {
    Model model =
        Model.assembler()
            .addUnparsedModel("alloy-proto-traits.smithy", ALLOY_PROTO_TRAITS)
            .addUnparsedModel("model.smithy", WELL_KNOWN_TYPES_MODEL)
            .assemble()
            .unwrap();
    FileManifest manifest = FileManifest.create(tempDir);

    new ProtoCodegenPlugin()
        .execute(
            PluginContext.builder()
                .model(model)
                .fileManifest(manifest)
                .settings(
                    ObjectNode.builder()
                        .withMember("service", Node.from("example.wellknown#WellKnownService"))
                        .build())
                .build());

    String proto = Files.readString(tempDir.resolve("Example/Wellknown/WellKnownService.proto"));
    assertTrue(proto.contains("import \"google/protobuf/empty.proto\";"), proto);
    assertTrue(proto.contains("import \"google/protobuf/timestamp.proto\";"), proto);
    assertTrue(proto.contains("import \"google/protobuf/struct.proto\";"), proto);
    assertTrue(proto.contains("rpc Ping (PingInput) returns (google.protobuf.Empty);"), proto);
    assertTrue(proto.contains("google.protobuf.Timestamp at = 1;"), proto);
    assertTrue(proto.contains("google.protobuf.Value payload = 2;"), proto);
  }

  @Test
  void executeFailsWhenMessageMembersReuseProtoIndex() {
    Model model =
        Model.assembler()
            .addUnparsedModel("alloy-proto-traits.smithy", ALLOY_PROTO_TRAITS)
            .addUnparsedModel("model.smithy", DUPLICATE_PROTO_INDEX_MODEL)
            .assemble()
            .unwrap();
    FileManifest manifest = FileManifest.create(tempDir);

    CodegenException ex =
        assertThrows(
            CodegenException.class,
            () ->
                new ProtoCodegenPlugin()
                    .execute(
                        PluginContext.builder()
                            .model(model)
                            .fileManifest(manifest)
                            .settings(
                                ObjectNode.builder()
                                    .withMember(
                                        "service", Node.from("example.invalid#InvalidService"))
                                    .build())
                            .build()));

    assertTrue(ex.getMessage().contains("Duplicate @protoIndex 1 in example.invalid#BrokenInput"));
  }

  /** The validation error the server returns; its shapes come from smithy-validation-model. */
  private static final String VALIDATION_MODEL =
      """
      $version: "2"

      namespace example.validated

      use alloy.proto#grpc
      use alloy.proto#protoIndex
      use smithy.framework#ValidationException

      @grpc
      service ValidatedService {
          version: "1"
          operations: [Register]
          errors: [ValidationException]
      }

      operation Register {
          input := {
              @required
              @protoIndex(1)
              name: String
          }
      }
      """;

  @Test
  void theFrameworkShapesNumberTheirFieldsByDeclarationOrder() throws Exception {
    Model model =
        Model.assembler()
            .discoverModels() // the validation model's shapes
            .addUnparsedModel("alloy-proto-traits.smithy", ALLOY_PROTO_TRAITS)
            .addUnparsedModel("model.smithy", VALIDATION_MODEL)
            .assemble()
            .unwrap();
    FileManifest manifest = FileManifest.create(tempDir);

    new ProtoCodegenPlugin()
        .execute(
            PluginContext.builder()
                .model(model)
                .fileManifest(manifest)
                .settings(
                    ObjectNode.builder()
                        .withMember("service", Node.from("example.validated#ValidatedService"))
                        .build())
                .build());

    // The error is never named in a message, so the service file does not import its file.
    String service = Files.readString(tempDir.resolve("Example/Validated/ValidatedService.proto"));
    assertFalse(service.contains("Smithy/Framework/types.proto"), service);
    String framework = Files.readString(tempDir.resolve("Smithy/Framework/types.proto"));
    assertTrue(
        framework.contains(
            "message ValidationException {\n"
                + "  string message = 1;\n"
                + "  repeated ValidationExceptionField field_list = 2;\n"
                + "}"),
        framework);
    assertTrue(
        framework.contains(
            "message ValidationExceptionField {\n"
                + "  string path = 1;\n"
                + "  string message = 2;\n"
                + "}"),
        framework);
  }
}
