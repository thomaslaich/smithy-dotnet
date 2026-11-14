package io.github.thomaslaich.nsmithy.codegen.core;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertSame;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import java.util.List;
import java.util.Optional;
import org.junit.jupiter.api.Test;
import software.amazon.smithy.codegen.core.CodegenException;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.shapes.OperationShape;
import software.amazon.smithy.model.shapes.ServiceShape;
import software.amazon.smithy.model.shapes.ShapeId;

final class ValidationErrorsTest {

  static final ShapeId WEATHER = ShapeId.from("example.weather#Weather");

  /** Operations with every kind of constrained input, and two the server never validates. */
  static final String MODEL =
      """
      $version: "2"
      namespace example.weather

      service Weather {
          version: "2024-01-01"
          operations: [GetCity, ListCities, GetCurrentTime, Describe, Lookup, Choose]
      }

      /// A pattern on a shape the input targets.
      operation GetCity {
          input := {
              @required
              cityId: CityId
          }
          errors: [NoSuchCity]
      }

      /// Optional members without constraints.
      operation ListCities {
          input := {
              nextToken: String
              pageSize: Integer
          }
      }

      /// No input at all.
      operation GetCurrentTime {
          output := {
              @required
              time: Timestamp
          }
      }

      /// A constraint two structures deep.
      operation Describe {
          input := { place: Place }
      }

      /// An enum shape is checked for membership.
      operation Lookup {
          input := { kind: Kind }
      }

      /// A string with the deprecated @enum trait is checked too.
      operation Choose {
          input := { legacy: Legacy }
      }

      @pattern("^[A-Za-z]+$")
      string CityId

      structure Place {
          coordinates: Coordinates
      }

      structure Coordinates {
          @range(min: -90, max: 90)
          latitude: Float
      }

      enum Kind {
          CITY
      }

      @enum([{ value: "old" }])
      string Legacy

      @error("client")
      structure NoSuchCity {}
      """;

  @Test
  void anOperationIsConstrainedWhenTheServerValidatesItsInput() {
    Model model = model(MODEL);

    assertTrue(constrained(model, "GetCity"), "a pattern on the member's target");
    assertTrue(constrained(model, "Describe"), "a range two structures deep");
    assertTrue(constrained(model, "Lookup"), "an enum shape");
    assertTrue(constrained(model, "Choose"), "the deprecated @enum trait");
    assertFalse(constrained(model, "ListCities"), "optional members without constraints");
    assertFalse(constrained(model, "GetCurrentTime"), "no input");
  }

  @Test
  void aServerRequiresConstrainedOperationsToDeclareTheError() {
    Model model = model(MODEL);
    ServiceShape service = model.expectShape(WEATHER, ServiceShape.class);

    CodegenException error =
        assertThrows(
            CodegenException.class, () -> ValidationErrors.requireDeclared(model, service));

    assertTrue(
        error
            .getMessage()
            .startsWith(
                "Operations example.weather#Choose, example.weather#Describe,"
                    + " example.weather#GetCity, example.weather#Lookup take input that is"
                    + " constrained"),
        error.getMessage());
    assertTrue(error.getMessage().contains("errors: [ValidationException]"), error.getMessage());
    assertTrue(error.getMessage().contains("smithy-validation-model"), error.getMessage());
    assertTrue(error.getMessage().contains("attachValidationException"), error.getMessage());
  }

  @Test
  void oneUndeclaredOperationIsNamedInTheSingular() {
    Model model =
        model(
            """
            $version: "2"
            namespace example.weather

            service Weather {
                version: "2024-01-01"
                operations: [GetCity]
            }

            operation GetCity {
                input := {
                    @required
                    cityId: String
                }
            }
            """);
    ServiceShape service = model.expectShape(WEATHER, ServiceShape.class);

    CodegenException error =
        assertThrows(
            CodegenException.class, () -> ValidationErrors.requireDeclared(model, service));

    assertTrue(
        error.getMessage().startsWith("Operation example.weather#GetCity takes input"),
        error.getMessage());
  }

  @Test
  void anOperationThatDeclaresTheErrorPassesTheCheck() {
    Model model =
        model(
            """
            $version: "2"
            namespace example.weather

            use smithy.framework#ValidationException

            service Weather {
                version: "2024-01-01"
                operations: [GetCity, ListCities]
            }

            operation GetCity {
                input := {
                    @required
                    cityId: String
                }
                errors: [ValidationException]
            }

            operation ListCities {
                input := { nextToken: String }
            }
            """);

    ValidationErrors.requireDeclared(model, model.expectShape(WEATHER, ServiceShape.class));
  }

  @Test
  void aServiceThatDeclaresTheErrorPassesTheCheckForEveryOperation() {
    Model model =
        model(
            """
            $version: "2"
            namespace example.weather

            use smithy.framework#ValidationException

            service Weather {
                version: "2024-01-01"
                operations: [GetCity, Describe]
                errors: [ValidationException]
            }

            operation GetCity {
                input := {
                    @required
                    cityId: String
                }
            }

            operation Describe {
                input := {
                    @length(min: 1)
                    place: String
                }
            }
            """);

    ValidationErrors.requireDeclared(model, model.expectShape(WEATHER, ServiceShape.class));
  }

  @Test
  void attachingDeclaresTheErrorOnConstrainedOperationsOnly() {
    Model attached = ValidationErrors.attach(model(MODEL));

    assertEquals(
        List.of(ShapeId.from("example.weather#NoSuchCity"), ShapeSupport.VALIDATION_EXCEPTION),
        operation(attached, "GetCity").getErrors().stream().sorted().toList());
    assertEquals(
        List.of(ShapeSupport.VALIDATION_EXCEPTION), operation(attached, "Describe").getErrors());
    assertEquals(
        List.of(ShapeSupport.VALIDATION_EXCEPTION), operation(attached, "Lookup").getErrors());
    assertEquals(
        List.of(ShapeSupport.VALIDATION_EXCEPTION), operation(attached, "Choose").getErrors());
    assertEquals(List.of(), operation(attached, "ListCities").getErrors());
    assertEquals(List.of(), operation(attached, "GetCurrentTime").getErrors());
    ValidationErrors.requireDeclared(attached, attached.expectShape(WEATHER, ServiceShape.class));
  }

  @Test
  void attachingLeavesAnOperationThatDeclaresTheErrorAlone() {
    Model model =
        model(
            """
            $version: "2"
            namespace example.weather

            use smithy.framework#ValidationException

            service Weather {
                version: "2024-01-01"
                operations: [GetCity]
            }

            operation GetCity {
                input := {
                    @required
                    cityId: String
                }
                errors: [ValidationException]
            }
            """);

    assertSame(model, ValidationErrors.attach(model));
  }

  @Test
  void attachingLeavesTheOperationsOfAServiceThatDeclaresTheErrorAlone() {
    Model model =
        model(
            """
            $version: "2"
            namespace example.weather

            use smithy.framework#ValidationException

            service Weather {
                version: "2024-01-01"
                operations: [GetCity]
                errors: [ValidationException]
            }

            operation GetCity {
                input := {
                    @required
                    cityId: String
                }
            }
            """);

    assertSame(model, ValidationErrors.attach(model));
  }

  @Test
  void attachingNeedsTheFrameworkShapesInTheModel() {
    Model model = Model.assembler().addUnparsedModel("model.smithy", MODEL).assemble().unwrap();

    CodegenException error =
        assertThrows(CodegenException.class, () -> ValidationErrors.attach(model));

    assertTrue(error.getMessage().contains("smithy-validation-model"), error.getMessage());
  }

  @Test
  void applyChecksAServerByDefaultAndAttachesWhenAsked() {
    Model model = model(MODEL);
    Optional<ShapeId> service = Optional.of(WEATHER);

    assertThrows(CodegenException.class, () -> ValidationErrors.apply(model, service, false, true));
    assertSame(model, ValidationErrors.apply(model, service, false, false));
    assertEquals(
        List.of(ShapeSupport.VALIDATION_EXCEPTION),
        operation(ValidationErrors.apply(model, service, true, true), "Lookup").getErrors());
  }

  @Test
  void theFrameworkShapesAreTheRuntimes() {
    assertTrue(ValidationErrors.isFrameworkShape(ShapeSupport.VALIDATION_EXCEPTION));
    assertTrue(ValidationErrors.isFrameworkShape(ValidationErrors.FIELD));
    assertTrue(ValidationErrors.isFrameworkShape(ValidationErrors.FIELD_LIST));
    assertFalse(ValidationErrors.isFrameworkShape(ShapeId.from("example.weather#NoSuchCity")));
  }

  private static boolean constrained(Model model, String operation) {
    return ValidationErrors.isConstrained(model, operation(model, operation));
  }

  static OperationShape operation(Model model, String name) {
    return model.expectShape(ShapeId.from("example.weather#" + name), OperationShape.class);
  }

  /** The sources together with the models on the class path: the validation model's shapes. */
  static Model model(String... sources) {
    var assembler = Model.assembler().discoverModels();
    for (int i = 0; i < sources.length; i++) {
      assembler.addUnparsedModel("model" + i + ".smithy", sources[i]);
    }
    return assembler.assemble().unwrap();
  }
}
