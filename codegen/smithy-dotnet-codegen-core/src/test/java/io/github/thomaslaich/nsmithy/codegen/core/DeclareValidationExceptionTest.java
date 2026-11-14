package io.github.thomaslaich.nsmithy.codegen.core;

import static io.github.thomaslaich.nsmithy.codegen.core.ValidationErrorsTest.MODEL;
import static io.github.thomaslaich.nsmithy.codegen.core.ValidationErrorsTest.WEATHER;
import static io.github.thomaslaich.nsmithy.codegen.core.ValidationErrorsTest.model;
import static io.github.thomaslaich.nsmithy.codegen.core.ValidationErrorsTest.operation;
import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertSame;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import java.util.List;
import org.junit.jupiter.api.Test;
import software.amazon.smithy.build.SmithyBuildException;
import software.amazon.smithy.build.TransformContext;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.shapes.ServiceShape;
import software.amazon.smithy.model.shapes.ShapeId;
import software.amazon.smithy.model.transform.ModelTransformer;

final class DeclareValidationExceptionTest {

  private static final ShapeId VALIDATION_EXCEPTION = ShapeSupport.VALIDATION_EXCEPTION;

  @Test
  void declaresTheErrorOnConstrainedOperationsOnly() {
    Model declared = transform(model(MODEL));

    assertEquals(
        List.of(ShapeId.from("example.weather#NoSuchCity"), VALIDATION_EXCEPTION),
        operation(declared, "GetCity").getIntroducedErrors().stream().sorted().toList());
    assertEquals(
        List.of(VALIDATION_EXCEPTION), operation(declared, "Describe").getIntroducedErrors());
    assertEquals(
        List.of(VALIDATION_EXCEPTION), operation(declared, "Lookup").getIntroducedErrors());
    assertEquals(
        List.of(VALIDATION_EXCEPTION), operation(declared, "Choose").getIntroducedErrors());
    assertEquals(List.of(), operation(declared, "ListCities").getIntroducedErrors());
    assertEquals(List.of(), operation(declared, "GetCurrentTime").getIntroducedErrors());
    ValidationErrors.requireDeclared(declared, declared.expectShape(WEATHER, ServiceShape.class));
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
            """);

    assertSame(model, transform(model));
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
            """);

    assertSame(model, transform(model));
  }

  @Test
  void needsTheFrameworkShapesInTheModel() {
    Model model = Model.assembler().addUnparsedModel("model.smithy", MODEL).assemble().unwrap();

    SmithyBuildException error = assertThrows(SmithyBuildException.class, () -> transform(model));

    assertTrue(error.getMessage().contains("smithy-validation-model"), error.getMessage());
  }

  @Test
  void isTheDeclareValidationExceptionTransform() {
    assertEquals("declareValidationException", new DeclareValidationException().getName());
  }

  private static Model transform(Model model) {
    return new DeclareValidationException()
        .transform(
            TransformContext.builder().model(model).transformer(ModelTransformer.create()).build());
  }
}
