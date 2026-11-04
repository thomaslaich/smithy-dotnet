using NSmithy.Core;
using NSmithy.Core.Serde;
using NSmithy.Core.Validation;

namespace NSmithy.Tests.Core;

public sealed class OperationSchemaTests
{
    private static readonly ShapeId Added = ShapeId.Parse("example#Added");

    private static OperationSchema<SmithyUnit, SmithyUnit> Operation() =>
        Schemas.Operation(
            ShapeId.Parse("example#Op"),
            Schemas.Unit,
            Schemas.Unit,
            [new Trait(ShapeId.Parse("example#marker"), Document.From("value"))],
            isStreaming: true
        );

    [Fact]
    public void WithErrorsAddsErrorsAndKeepsTheRest()
    {
        var operation = Operation();

        var copy = operation.WithErrors([
            Schemas.OperationError(Added, ValidationExceptionSchema.Schema, 503),
        ]);

        Assert.Contains(copy.Errors, error => error.Id == Added && error.HttpStatusCode == 503);
        Assert.DoesNotContain(operation.Errors, error => error.Id == Added);
        Assert.Equal(operation.Id, copy.Id);
        Assert.Same(operation.Input, copy.Input);
        Assert.Same(operation.Output, copy.Output);
        Assert.True(copy.HasTrait(ShapeId.Parse("example#marker")));
        Assert.True(copy.IsStreaming);
    }

    [Fact]
    public void WithErrorsKeepsTheRegistrationOfAnErrorAlreadyDeclared()
    {
        var copy = Operation()
            .WithErrors([
                Schemas.OperationError(
                    ValidationExceptionSchema.Id,
                    ValidationExceptionSchema.Schema,
                    599
                ),
            ]);

        Assert.Equal(400, Assert.Single(copy.Errors).HttpStatusCode);
    }
}
