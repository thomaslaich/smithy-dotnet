package io.github.thomaslaich.nsmithy.csharp.codegen;

import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import io.github.thomaslaich.nsmithy.csharp.codegen.integrations.CSharpIntegration;
import java.nio.file.Path;
import java.util.List;
import java.util.Set;
import org.junit.jupiter.api.Test;
import software.amazon.smithy.build.MockManifest;
import software.amazon.smithy.codegen.core.CodegenException;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.node.ExpectationNotMetException;
import software.amazon.smithy.model.node.Node;
import software.amazon.smithy.model.node.ObjectNode;
import software.amazon.smithy.model.shapes.ServiceShape;
import software.amazon.smithy.model.shapes.ShapeId;

class ShapeClosureTest {

  private static final String MODEL =
      """
      $version: "2"

      metadata shapeClosures = [
          {
              id: "example.club#events"
              includeBySelector: "structure[trait|tags|(values) = event]"
          }
          {
              id: "example.club#levels"
              includeBySelector: "enum"
          }
          {
              id: "example.club#all"
              includeNamespaces: ["example.club"]
          }
      ]

      namespace example.club

      service Club {
          version: "2024-01-01"
          operations: [Join]
      }

      operation Join {
          input := { name: String }
      }

      @tags(["event"])
      structure MemberJoined {
          name: String
          level: Level
      }

      enum Level {
          GOLD
          SILVER
      }

      structure Unused {}
      """;

  private static final Path CLIENT = Path.of("Example/Club/Club.Client.g.cs");
  private static final Path SERVICE_SCHEMA = Path.of("Example/Club/Club.Schema.g.cs");
  private static final Path JOIN_INPUT = Path.of("Example/Club/JoinInput.g.cs");
  private static final Path MEMBER_JOINED = Path.of("Example/Club/MemberJoined.g.cs");
  private static final Path LEVEL = Path.of("Example/Club/Level.g.cs");
  private static final Path UNUSED = Path.of("Example/Club/Unused.g.cs");

  @Test
  void aClosureAloneGeneratesOnlyItsTypes() {
    MockManifest manifest = generate(settings(null, "example.club#events"), List.of());

    assertTrue(manifest.hasFile(MEMBER_JOINED));
    assertTrue(manifest.hasFile(LEVEL));
    assertFalse(manifest.hasFile(JOIN_INPUT));
    assertFalse(manifest.hasFile(CLIENT));
    assertFalse(manifest.hasFile(SERVICE_SCHEMA));
  }

  @Test
  void aClosureWithItsServiceGeneratesBoth() {
    MockManifest manifest = generate(settings("example.club#Club", "example.club#all"), List.of());

    assertTrue(manifest.hasFile(CLIENT));
    assertTrue(manifest.hasFile(JOIN_INPUT));
    assertTrue(manifest.hasFile(MEMBER_JOINED));
    assertTrue(manifest.hasFile(UNUSED));
  }

  @Test
  void aServiceAloneGeneratesOnlyWhatItReaches() {
    MockManifest manifest = generate(settings("example.club#Club", null), List.of());

    assertTrue(manifest.hasFile(CLIENT));
    assertFalse(manifest.hasFile(MEMBER_JOINED));
  }

  @Test
  void anIntegrationAddsShapesToTheService() {
    MockManifest manifest =
        generate(
            settings("example.club#Club", null),
            List.of(new AddingIntegration(ShapeId.from("example.club#MemberJoined"))));

    assertTrue(manifest.hasFile(CLIENT));
    assertTrue(manifest.hasFile(JOIN_INPUT));
    assertTrue(manifest.hasFile(MEMBER_JOINED));
    assertTrue(manifest.hasFile(LEVEL));
    assertFalse(manifest.hasFile(UNUSED));
  }

  @Test
  void anIntegrationAddsShapesToAConfiguredClosure() {
    MockManifest manifest =
        generate(
            settings("example.club#Club", "example.club#all"),
            List.of(new AddingIntegration(ShapeId.from("example.club#MemberJoined"))));

    assertTrue(manifest.hasFile(CLIENT));
    assertTrue(manifest.hasFile(UNUSED));
  }

  @Test
  void aConfiguredClosureMustContainTheService() {
    CodegenException exception =
        assertThrows(
            CodegenException.class,
            () ->
                generate(
                    settings("example.club#Club", "example.club#levels"),
                    List.of(new AddingIntegration(ShapeId.from("example.club#MemberJoined")))));

    assertTrue(exception.getMessage().contains("example.club#levels"));
  }

  @Test
  void anUnknownClosureIsRejected() {
    CodegenException exception =
        assertThrows(
            CodegenException.class,
            () -> generate(settings(null, "example.club#missing"), List.of()));

    assertTrue(exception.getMessage().contains("example.club#missing"));
  }

  @Test
  void anAddedShapeMustExist() {
    CodegenException exception =
        assertThrows(
            CodegenException.class,
            () ->
                generate(
                    settings("example.club#Club", null),
                    List.of(new AddingIntegration(ShapeId.from("example.club#Missing")))));

    assertTrue(exception.getMessage().contains("example.club#Missing"));
  }

  @Test
  void settingsNeedAServiceOrAClosure() {
    assertThrows(ExpectationNotMetException.class, () -> settings(null, null));
  }

  private static CSharpSettings settings(String service, String closure) {
    ObjectNode.Builder node = ObjectNode.builder().withMember("baseNamespace", Node.from(""));
    if (service != null) {
      node.withMember("service", Node.from(service));
    }
    if (closure != null) {
      node.withMember("closure", Node.from(closure));
    }
    return CSharpSettings.fromNode(node.build());
  }

  private static MockManifest generate(
      CSharpSettings settings, List<CSharpIntegration> integrations) {
    Model model = Model.assembler().addUnparsedModel("club.smithy", MODEL).assemble().unwrap();
    MockManifest manifest = new MockManifest();
    CSharpCodegenPlugin.generate(model, settings, manifest, integrations);
    return manifest;
  }

  private static final class AddingIntegration implements CSharpIntegration {
    private final ShapeId shape;

    AddingIntegration(ShapeId shape) {
      this.shape = shape;
    }

    @Override
    public Set<ShapeId> additionalShapes(
        Model model, CSharpSettings settings, ServiceShape service) {
      return Set.of(shape);
    }
  }
}
