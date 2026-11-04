using NSmithy.Core;
using NSmithy.Core.Serde;
using NSmithy.Core.Validation;

namespace NSmithy.Tests.Core;

public sealed class ServiceSchemaTests
{
    private static readonly ShapeId ServiceError = ShapeId.Parse("example#ServiceError");

    private static OperationSchema<SmithyUnit, SmithyUnit> Operation() =>
        Schemas.Operation(ShapeId.Parse("example#Op"), Schemas.Unit, Schemas.Unit);

    private static ServiceSchema Service(params IOperationErrorSchema[] errors) =>
        Schemas.Service(ShapeId.Parse("example#Service"), "1", errors: errors);

    [Fact]
    public void ErrorsOfAddsTheServiceErrorsToTheOperations()
    {
        var operation = Operation();
        var service = Service(
            Schemas.OperationError(ServiceError, ValidationExceptionSchema.Schema, 503)
        );

        var errors = service.ErrorsOf(operation);

        Assert.Equal(operation.Errors.Count + 1, errors.Count);
        Assert.Contains(errors, error => error.Id == ServiceError && error.HttpStatusCode == 503);
        Assert.DoesNotContain(operation.Errors, error => error.Id == ServiceError);
    }

    [Fact]
    public void ErrorsOfKeepsTheOperationsRegistrationOfAnErrorBothDeclare()
    {
        var service = Service(
            Schemas.OperationError(
                ValidationExceptionSchema.Id,
                ValidationExceptionSchema.Schema,
                599
            )
        );

        var error = Assert.Single(service.ErrorsOf(Operation()));

        Assert.Equal(400, error.HttpStatusCode);
    }

    // A response without an error name goes to the first error with its status: a service's 400
    // must win over the implicit validation error, as it does when listed on the operation.
    [Fact]
    public void ErrorsOfSortsByShapeIdWithTheValidationErrorLast()
    {
        var operation = Schemas.Operation(
            ShapeId.Parse("example#Op"),
            Schemas.Unit,
            Schemas.Unit,
            [
                Schemas.OperationError(
                    ShapeId.Parse("example#Middle"),
                    ValidationExceptionSchema.Schema,
                    400
                ),
            ]
        );
        var service = Service(
            Schemas.OperationError(
                ShapeId.Parse("example#Zulu"),
                ValidationExceptionSchema.Schema,
                400
            ),
            Schemas.OperationError(
                ShapeId.Parse("example#Alpha"),
                ValidationExceptionSchema.Schema,
                400
            )
        );

        Assert.Equal(
            [
                "example#Alpha",
                "example#Middle",
                "example#Zulu",
                ValidationExceptionSchema.Id.ToString(),
            ],
            service.ErrorsOf(operation).Select(error => error.Id.ToString())
        );
    }

    [Fact]
    public void ErrorsOfReturnsTheOperationsErrorsWithoutServiceErrors()
    {
        var operation = Operation();

        Assert.Same(operation.Errors, Service().ErrorsOf(operation));
    }
}
