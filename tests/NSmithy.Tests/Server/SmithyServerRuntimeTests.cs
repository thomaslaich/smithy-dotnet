using System.Text;
using NSmithy.Core;
using NSmithy.Core.Serde;
using NSmithy.Core.Validation;
using NSmithy.Http;
using NSmithy.Protocols.RestJson;
using NSmithy.Server;
using Nsmithy.Tests.Server;
using Nsmithy.Tests.Server.Schemas;

namespace NSmithy.Tests.Server;

public sealed class SmithyServerRuntimeTests
{
    [Fact]
    public async Task DispatchReturnsValidationExceptionResponseForInvalidInput()
    {
        var protocol = Protocol();
        var request = Request(new CreateUserInput("ab"));
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
        Assert.Contains("/name", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DispatchInvokesHandlerForValidInput()
    {
        var protocol = Protocol();
        var request = Request(new CreateUserInput("Ada"));

        var response = await new SmithyServerRuntime().DispatchAsync(
            protocol,
            request,
            (input, _) => Task.FromResult(new CreateUserOutput(input.Name))
        );

        Assert.Equal(200, response.StatusCode);
    }

    public static TheoryData<string> ModeledErrorHandlers => ["throws", "async", "faulted"];

    [Theory]
    [MemberData(nameof(ModeledErrorHandlers))]
    public async Task DispatchReturnsTheModeledErrorAHandlerRaises(string raising)
    {
        var response = await new SmithyServerRuntime().DispatchAsync(
            Protocol(),
            Request(new CreateUserInput("Ada")),
            Raise<CreateUserOutput>(raising, new UserExists("Ada exists."))
        );

        Assert.Equal(409, response.StatusCode);
        Assert.Equal("UserExists", response.Headers["X-Amzn-Errortype"].Single());
        Assert.Contains("Ada exists.", await ReadBodyAsync(response), StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(ModeledErrorHandlers))]
    public async Task DispatchRethrowsAnUnmodeledExceptionAHandlerRaises(string raising)
    {
        var unmodeled = new InvalidOperationException("Broken.");

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new SmithyServerRuntime().DispatchAsync(
                Protocol(),
                Request(new CreateUserInput("Ada")),
                Raise<CreateUserOutput>(raising, unmodeled)
            )
        );

        Assert.Same(unmodeled, thrown);
    }

    [Fact]
    public async Task DispatchRethrowsACanceledHandler()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new SmithyServerRuntime().DispatchAsync(
                Protocol(),
                Request(new CreateUserInput("Ada")),
                (_, _) => Task.FromCanceled<CreateUserOutput>(cancellation.Token)
            )
        );
    }

    [Fact]
    public void OperationSchemaCarriesImplicitValidationError()
    {
        var error = Assert.Single(
            CreateUserSchema.Schema.Errors,
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

    [Fact]
    public async Task DispatchReturnsValidationExceptionForMissingRequiredMember()
    {
        var protocol = Protocol();
        var request = new SmithyHttpRequest(HttpMethod.Post, "/users")
        {
            Body = new SmithyHttpBody.Bytes(Encoding.UTF8.GetBytes("{}")),
            ContentType = "application/json",
        };
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

        // The codec rejects the payload before the compiled validator runs; it is still the
        // caller's mistake, so it must not surface as a 500.
        Assert.False(handled);
        Assert.Equal(400, response.StatusCode);
        Assert.Equal("ValidationException", response.Headers["X-Amzn-Errortype"].Single());
        var body = await ReadBodyAsync(response);
        Assert.Contains("/name", body, StringComparison.Ordinal);
    }

    // The three ways a handler raises: throwing before it returns a task, throwing inside an async
    // method, and returning a faulted task.
    private static Func<CreateUserInput, CancellationToken, Task<T>> Raise<T>(
        string raising,
        Exception exception
    ) =>
        raising switch
        {
            "throws" => (_, _) => throw exception,
            "async" => async (_, _) =>
            {
                await Task.Yield();
                throw exception;
            },
            _ => (_, _) => Task.FromException<T>(exception),
        };

    private static IServiceProtocol Service() =>
        new RestJson1Protocol().ForService(FixturesSchema.Schema);

    private static IServerOperationProtocol<CreateUserInput, CreateUserOutput> Protocol() =>
        Service().ForServerOperation(CreateUserSchema.Schema);

    private static SmithyHttpRequest Request(CreateUserInput input) =>
        Service().ForClientOperation(CreateUserSchema.Schema).SerializeRequest(input);

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
