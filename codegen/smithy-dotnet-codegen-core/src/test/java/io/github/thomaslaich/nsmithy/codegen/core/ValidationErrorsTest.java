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
import software.amazon.smithy.model.shapes.StructureShape;
import software.amazon.smithy.model.traits.ErrorTrait;
import software.amazon.smithy.model.traits.HttpErrorTrait;

final class ValidationErrorsTest {

  private static final ShapeId VALIDATION_EXCEPTION = ShapeSupport.VALIDATION_EXCEPTION;
  private static final ShapeId WEATHER = ShapeId.from("example.weather#Weather");

  private static final String MODEL =
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
  void attachesTheErrorToConstrainedOperationsOnly() {
    Model attached = ValidationErrors.attach(model(MODEL));

    assertEquals(
        List.of(ShapeId.from("example.weather#NoSuchCity"), VALIDATION_EXCEPTION),
        operation(attached, "GetCity").getErrors().stream().sorted().toList());
    assertEquals(List.of(VALIDATION_EXCEPTION), operation(attached, "Describe").getErrors());
    assertEquals(List.of(VALIDATION_EXCEPTION), operation(attached, "Lookup").getErrors());
    assertEquals(List.of(VALIDATION_EXCEPTION), operation(attached, "Choose").getErrors());
    assertEquals(List.of(), operation(attached, "ListCities").getErrors());
    assertEquals(List.of(), operation(attached, "GetCurrentTime").getErrors());
  }

  @Test
  void addsTheFrameworkShapesWhenTheModelLacksThem() {
    Model attached = ValidationErrors.attach(model(MODEL));

    StructureShape error = attached.expectShape(VALIDATION_EXCEPTION, StructureShape.class);
    assertEquals("client", error.expectTrait(ErrorTrait.class).getValue());
    assertEquals(400, error.expectTrait(HttpErrorTrait.class).getCode());
    assertTrue(error.getMember("message").orElseThrow().isRequired());
    assertEquals(
        ValidationErrors.FIELD_LIST, error.getMember("fieldList").orElseThrow().getTarget());
    assertEquals(
        ValidationErrors.FIELD,
        attached
            .expectShape(ValidationErrors.FIELD_LIST)
            .asListShape()
            .orElseThrow()
            .getMember()
            .getTarget());
    StructureShape field = attached.expectShape(ValidationErrors.FIELD, StructureShape.class);
    assertTrue(field.getMember("path").orElseThrow().isRequired());
    assertTrue(field.getMember("message").orElseThrow().isRequired());
  }

  @Test
  void keepsTheModelsOwnFrameworkShapes() {
    Model model =
        model(
            MODEL,
            """
            $version: "2"
            namespace smithy.framework

            @error("client")
            @httpError(400)
            structure ValidationException {
                @required
                message: String
                fieldList: ValidationExceptionFieldList
            }

            list ValidationExceptionFieldList {
                member: ValidationExceptionField
            }

            structure ValidationExceptionField {
                @required
                path: String
                @required
                message: String
            }
            """);

    Model attached = ValidationErrors.attach(model);

    assertSame(model.expectShape(VALIDATION_EXCEPTION), attached.expectShape(VALIDATION_EXCEPTION));
    assertTrue(operation(attached, "GetCity").getErrors().contains(VALIDATION_EXCEPTION));
  }

  @Test
  void leavesAnOperationThatDeclaresTheErrorAlone() {
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
            """,
            FRAMEWORK);

    Model attached = ValidationErrors.attach(model);

    assertEquals(List.of(VALIDATION_EXCEPTION), operation(attached, "GetCity").getErrors());
  }

  @Test
  void leavesTheOperationsOfAServiceThatDeclaresTheErrorAlone() {
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
            """,
            FRAMEWORK);

    Model attached = ValidationErrors.attach(model);

    assertEquals(List.of(), operation(attached, "GetCity").getErrors());
    assertTrue(
        operation(attached, "GetCity")
            .getErrors(attached.expectShape(WEATHER, ServiceShape.class))
            .contains(VALIDATION_EXCEPTION));
  }

  @Test
  void returnsTheModelUnchangedWhenNothingIsConstrained() {
    Model model =
        model(
            """
            $version: "2"
            namespace example.weather

            service Weather {
                version: "2024-01-01"
                operations: [ListCities]
            }

            operation ListCities {
                input := { nextToken: String }
            }
            """);

    Model attached = ValidationErrors.attach(model);

    assertEquals(List.of(), operation(attached, "ListCities").getErrors());
    assertTrue(attached.getShape(VALIDATION_EXCEPTION).isPresent(), "the shapes are still added");
  }

  @Test
  void aServerThatDoesNotAttachMustDeclareTheErrorOnConstrainedOperations() {
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
    assertTrue(error.getMessage().contains("attachValidationException"), error.getMessage());
  }

  @Test
  void aServerThatDeclaresTheErrorPassesTheCheck() {
    Model model =
        model(
            """
            $version: "2"
            namespace example.weather

            use smithy.framework#ValidationException

            service Weather {
                version: "2024-01-01"
                operations: [GetCity, ListCities]
                errors: [ValidationException]
            }

            operation GetCity {
                input := {
                    @required
                    cityId: String
                }
            }

            operation ListCities {
                input := { nextToken: String }
            }
            """,
            FRAMEWORK);
    ServiceShape service = model.expectShape(WEATHER, ServiceShape.class);

    ValidationErrors.requireDeclared(model, service);
  }

  @Test
  void applyAttachesByDefaultAndChecksOnlyAServerThatOptsOut() {
    Model model = model(MODEL);

    assertTrue(
        operation(ValidationErrors.apply(model, Optional.of(WEATHER), true, true), "GetCity")
            .getErrors()
            .contains(VALIDATION_EXCEPTION));
    assertSame(model, ValidationErrors.apply(model, Optional.of(WEATHER), false, false));
    assertSame(model, ValidationErrors.apply(model, Optional.empty(), false, true));
    assertThrows(
        CodegenException.class,
        () -> ValidationErrors.apply(model, Optional.of(WEATHER), false, true));
  }

  @Test
  void theFrameworkShapesAreTheRuntimes() {
    assertTrue(ValidationErrors.isFrameworkShape(VALIDATION_EXCEPTION));
    assertTrue(ValidationErrors.isFrameworkShape(ValidationErrors.FIELD));
    assertTrue(ValidationErrors.isFrameworkShape(ValidationErrors.FIELD_LIST));
    assertFalse(ValidationErrors.isFrameworkShape(ShapeId.from("example.weather#NoSuchCity")));
  }

  private static final String FRAMEWORK =
      """
      $version: "2"
      namespace smithy.framework

      @error("client")
      @httpError(400)
      structure ValidationException {
          @required
          message: String
          fieldList: ValidationExceptionFieldList
      }

      list ValidationExceptionFieldList {
          member: ValidationExceptionField
      }

      structure ValidationExceptionField {
          @required
          path: String
          @required
          message: String
      }
      """;

  private static boolean constrained(Model model, String operation) {
    return ValidationErrors.isConstrained(model, operation(model, operation));
  }

  private static OperationShape operation(Model model, String name) {
    return model.expectShape(ShapeId.from("example.weather#" + name), OperationShape.class);
  }

  private static Model model(String... sources) {
    var assembler = Model.assembler();
    for (int i = 0; i < sources.length; i++) {
      assembler.addUnparsedModel("model" + i + ".smithy", sources[i]);
    }
    return assembler.assemble().unwrap();
  }
}
