using System.Text;
using NSmithy.Core;
using NSmithy.Core.Serde;
using NSmithy.Core.Validation;
using NSmithy.Http;
using NSmithy.Protocols.Rest;
using NSmithy.Protocols.RestJson;
using NSmithy.Server;

namespace NSmithy.Tests.Server;

public sealed class SmithyServerRuntimeTests
{
    private static readonly ShapeId LengthTrait = new("smithy.api", "length");

    public sealed record CreateUserInput(string Name);

    public sealed class CreateUserInputBuilder
    {
        public string? Name { get; set; }
    }

    public sealed record CreateUserOutput(string Name);

    public sealed class CreateUserOutputBuilder
    {
        public string? Name { get; set; }
    }

    [Fact]
    public async Task DispatchReturnsValidationExceptionResponseForInvalidInput()
    {
        var protocol = Protocol();
        var request = protocol.SerializeRequest(new CreateUserInput("ab"));
        var handled = false;

        var response = await new SmithyServerRuntime().DispatchAsync(
            protocol,
            request,
            (input, _) =>
            {
                handled = true;
                return Task.FromResult(new CreateUserOutput(input.Name));
            }
        );

        Assert.False(handled);
        Assert.Equal(400, response.StatusCode);
        Assert.Equal("ValidationException", response.Headers["X-Amzn-Errortype"].Single());
        var body = await ReadBodyAsync(response);
        Assert.Contains("$.name", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DispatchInvokesHandlerForValidInput()
    {
        var protocol = Protocol();
        var request = protocol.SerializeRequest(new CreateUserInput("Ada"));

        var response = await new SmithyServerRuntime().DispatchAsync(
            protocol,
            request,
            (input, _) => Task.FromResult(new CreateUserOutput(input.Name))
        );

        Assert.Equal(200, response.StatusCode);
    }

    [Fact]
    public void OperationSchemaCarriesImplicitValidationError()
    {
        var error = Assert.Single(
            Operation().Errors,
            error => error.Id == ValidationExceptionSchema.Id
        );

        Assert.Equal(400, error.HttpStatusCode);
    }

    [Fact]
    public void ExplicitlyModeledValidationErrorIsNotDuplicated()
    {
        var operation = Schemas.Operation(
            new ShapeId("test", "CreateUser"),
            Schemas.String,
            Schemas.String,
            errors: [ValidationExceptionSchema.OperationError]
        );

        Assert.Single(operation.Errors, error => error.Id == ValidationExceptionSchema.Id);
    }

    private static OperationSchema<CreateUserInput, CreateUserOutput> Operation()
    {
        var inputSchema = Schemas
            .Structure<CreateUserInput, CreateUserInputBuilder>(
                new ShapeId("test", "CreateUserInput")
            )
            .Required(
                "name",
                static value => value.Name,
                static (builder, value) => builder.Name = value,
                Schemas.String,
                [
                    new Trait(
                        LengthTrait,
                        Document.From(
                            new Dictionary<string, Document>(StringComparer.Ordinal)
                            {
                                ["min"] = Document.From(3),
                            }
                        )
                    ),
                ]
            )
            .Build(
                static () => new CreateUserInputBuilder(),
                static builder => new CreateUserInput(builder.Name!)
            );
        var outputSchema = Schemas
            .Structure<CreateUserOutput, CreateUserOutputBuilder>(
                new ShapeId("test", "CreateUserOutput")
            )
            .Required(
                "name",
                static value => value.Name,
                static (builder, value) => builder.Name = value,
                Schemas.String
            )
            .Build(
                static () => new CreateUserOutputBuilder(),
                static builder => new CreateUserOutput(builder.Name!)
            );
        return Schemas.Operation(
            new ShapeId("test", "CreateUser"),
            inputSchema,
            outputSchema,
            traits: [RestTraits.HttpTrait("POST", "/users")]
        );
    }

    private static IOperationProtocol<CreateUserInput, CreateUserOutput> Protocol() =>
        new RestJson1Protocol()
            .ForService(Schemas.Service(ShapeId.Parse("test#Service")))
            .ForOperation(Operation());

    private static async Task<string> ReadBodyAsync(SmithyHttpServerResponse response)
    {
        using var stream = new MemoryStream();
        await foreach (var chunk in response.Body)
        {
            stream.Write(chunk.Span);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
