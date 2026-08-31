using NSmithy.Core;
using NSmithy.Core.Serde;
using NSmithy.Server;

namespace NSmithy.Tests.Server;

public sealed class ServiceOperationCatalogTests
{
    private static readonly ShapeId OperationId = new("example", "CountLetters");

    [Fact]
    public async Task CatalogEnumeratesLooksUpAndInvokesTypedOperations()
    {
        var serviceSchema = Schemas.Service(new ShapeId("example", "TextService"), "2026-08-30");
        var operationSchema = Schemas.Operation(OperationId, Schemas.String, Schemas.Integer);
        CancellationToken observedCancellationToken = default;
        var operation = ServiceOperation.Create(
            operationSchema,
            (string input, CancellationToken cancellationToken) =>
            {
                observedCancellationToken = cancellationToken;
                return Task.FromResult(input.Length);
            }
        );
        var catalog = new ServiceOperationCatalog(serviceSchema, operation);
        using var cancellation = new CancellationTokenSource();

        var result = await catalog
            .GetOperation(OperationId)
            .InvokeAsync("Smithy", cancellation.Token);

        var untypedSchema = Assert.IsAssignableFrom<IOperationSchema>(operationSchema);
        Assert.Same(serviceSchema, catalog.Schema);
        Assert.Same(operation, Assert.Single(catalog.Operations));
        Assert.True(catalog.TryGetOperation(OperationId, out var found));
        Assert.Same(operation, found);
        Assert.Same(Schemas.String, untypedSchema.Input);
        Assert.Same(Schemas.Integer, untypedSchema.Output);
        Assert.Equal(6, result);
        Assert.Equal(cancellation.Token, observedCancellationToken);
    }

    [Fact]
    public async Task OperationRejectsInputOfTheWrongRuntimeType()
    {
        var operation = ServiceOperation.Create(
            Schemas.Operation(OperationId, Schemas.String, Schemas.Integer),
            static (string input, CancellationToken _) => Task.FromResult(input.Length)
        );

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            operation.InvokeAsync(42)
        );

        Assert.Equal("input", exception.ParamName);
        Assert.Contains(OperationId.ToString(), exception.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(string).FullName!, exception.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(int).FullName!, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OperationValidatesInputBeforeInvokingHandler()
    {
        var constrainedString = new StringSchema(
            new ShapeId("example", "ConstrainedString"),
            [
                new Trait(
                    ShapeId.Parse("smithy.api#length"),
                    Document.From(new Dictionary<string, Document> { ["min"] = Document.From(3m) })
                ),
            ]
        );
        var invoked = false;
        var operation = ServiceOperation.Create(
            Schemas.Operation(OperationId, constrainedString, Schemas.Integer),
            (string input, CancellationToken _) =>
            {
                invoked = true;
                return Task.FromResult(input.Length);
            }
        );

        await Assert.ThrowsAsync<NSmithy.Core.Validation.ValidationException>(() =>
            operation.InvokeAsync("no")
        );

        Assert.False(invoked);
    }

    [Fact]
    public void CatalogRejectsDuplicateOperationIds()
    {
        var schema = Schemas.Operation(OperationId, Schemas.String, Schemas.Integer);
        var first = ServiceOperation.Create(
            schema,
            static (string input, CancellationToken _) => Task.FromResult(input.Length)
        );
        var second = ServiceOperation.Create(
            schema,
            static (string input, CancellationToken _) => Task.FromResult(input.Length * 2)
        );

        var exception = Assert.Throws<ArgumentException>(() =>
            new ServiceOperationCatalog(
                Schemas.Service(new ShapeId("example", "TextService")),
                first,
                second
            )
        );

        Assert.Equal("operations", exception.ParamName);
        Assert.Contains(OperationId.ToString(), exception.Message, StringComparison.Ordinal);
    }
}
