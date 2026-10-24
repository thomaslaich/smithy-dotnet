package io.github.thomaslaich.nsmithy.csharp.codegen.validation;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertTrue;

import java.util.List;
import org.junit.jupiter.api.Test;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.validation.Severity;
import software.amazon.smithy.model.validation.ValidationEvent;

final class GrpcEventStreamValidatorTest {

  private static final String GRPC_TRAIT =
      """
      $version: "2"
      namespace alloy.proto

      @trait(selector: "service")
      @protocolDefinition
      structure grpc {}
      """;

  @Test
  void rejectsSiblingsOfAnEventStream() {
    List<ValidationEvent> events =
        errors(
            """
            $version: "2"
            namespace example.streaming

            use alloy.proto#grpc

            @grpc
            service StreamingService {
                version: "1"
                operations: [Chat]
            }

            operation Chat {
                input := {
                    room: String
                    events: ChatEvent
                }
                output := {
                    events: ChatEvent
                }
            }

            @streaming
            union ChatEvent {
                message: MessageEvent
            }

            structure MessageEvent {
                text: String
            }
            """);

    assertEquals(1, events.size(), events.toString());
    String message = events.get(0).getMessage();
    assertTrue(
        message.contains(
            "gRPC event-stream operation example.streaming#Chat input shape"
                + " example.streaming#ChatInput must contain exactly one event-stream member"),
        message);
    assertTrue(message.contains("Members: events (event stream), room."), message);
  }

  @Test
  void ignoresServicesWithoutGrpc() {
    assertTrue(
        errors(
                """
                $version: "2"
                namespace example.streaming

                service StreamingService {
                    version: "1"
                    operations: [Chat]
                }

                operation Chat {
                    input := {
                        room: String
                        events: ChatEvent
                    }
                }

                @streaming
                union ChatEvent {
                    message: MessageEvent
                }

                structure MessageEvent {
                    text: String
                }
                """)
            .isEmpty());
  }

  private static List<ValidationEvent> errors(String model) {
    return Model.assembler()
        .addUnparsedModel("grpc.smithy", GRPC_TRAIT)
        .addUnparsedModel("model.smithy", model)
        .assemble()
        .getValidationEvents(Severity.ERROR)
        .stream()
        .filter(event -> event.getId().equals("GrpcEventStream"))
        .toList();
  }
}
