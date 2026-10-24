using NSmithy.Core.Serde;
using NSmithy.Core.Validation;
using NSmithy.Http;

namespace NSmithy.Server;

/// <summary>
/// The shared, host-agnostic server runtime: the dual of the client runtime's execution core. It
/// owns the dispatch algorithm — deserialize the request, invoke the handler, serialize the
/// response or a modeled error — for every protocol and operation shape, so generated code and host
/// adapters carry none of it. It takes a neutral <see cref="SmithyHttpRequest"/> and returns a
/// neutral <see cref="SmithyHttpServerResponse"/>; a host adapter owns conversion to and from the host
/// framework's types. Instance-based so server interceptors and telemetry can attach here later, as
/// they do on <c>SmithyClientRuntime</c>.
/// </summary>
public sealed class SmithyServerRuntime
{
    /// <summary>The runtime host adapters use when the application registers none.</summary>
    public static SmithyServerRuntime Default { get; } = new();

    /// <summary>
    /// Dispatches an operation: deserialize, invoke, serialize — with the operation's modeled
    /// errors caught once, for every protocol. An unmodeled exception rethrows (surfaced as a 500
    /// by the host).
    /// </summary>
    public async Task<SmithyHttpServerResponse> DispatchAsync<TInput, TOutput>(
        IServerOperationProtocol<TInput, TOutput> protocol,
        SmithyHttpRequest request,
        Func<TInput, CancellationToken, Task<TOutput>> handler,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(protocol);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(handler);

        TInput input;
        try
        {
            input = await protocol
                .DeserializeRequestAsync(request, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (MissingRequiredMemberException exception)
        {
            // A required member absent from the payload fails in the codec, before the compiled
            // validator ever sees the input. It is still a constraint violation, so it gets the
            // same modeled response rather than surfacing as a server fault.
            return SerializeRequestFailure(
                protocol,
                ValidationException.FromMissingRequiredMember(exception)
            );
        }
        catch (MalformedRequestException exception)
        {
            // Bytes that never became modeled input at all: an unreadable body, a media type the
            // operation does not accept. Also the caller's mistake, so also a structured 4xx.
            return SerializeRequestFailure(protocol, exception);
        }

        if (Validate(protocol.InputValidator, input) is { } invalid)
        {
            return SerializeRequestFailure(protocol, invalid);
        }

        try
        {
            var output = await InvokeHandlerAsync(handler, input, cancellationToken)
                .ConfigureAwait(false);
            return protocol.SerializeResponse(output, cancellationToken);
        }
        catch (Exception exception)
        {
            if (protocol.TrySerializeError(exception, out var errorResponse))
            {
                return errorResponse;
            }

            throw;
        }
    }

    /// <summary>
    /// Validates an already-decoded input and calls the handler, for adapters that hold no wire
    /// request, such as MCP after decoding tool arguments. A constraint violation throws
    /// <see cref="ValidationException"/>; the handler's exceptions propagate.
    /// </summary>
    public async Task<TOutput> InvokeAsync<TInput, TOutput>(
        ISmithyValidator<TInput>? validator,
        TInput input,
        Func<TInput, CancellationToken, Task<TOutput>> handler,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(handler);
        if (Validate(validator, input) is { } invalid)
        {
            throw invalid;
        }

        return await InvokeHandlerAsync(handler, input, cancellationToken).ConfigureAwait(false);
    }

    private static ValidationException? Validate<TInput>(
        ISmithyValidator<TInput>? validator,
        TInput input
    )
    {
        if (validator is null)
        {
            return null;
        }

        var errors = validator.GetErrors(input);
        return errors.Count > 0 ? ValidationException.FromErrors(errors) : null;
    }

    // Every handler call, from every adapter, passes through here: the step server interceptors
    // and telemetry attach to.
    private static Task<TOutput> InvokeHandlerAsync<TInput, TOutput>(
        Func<TInput, CancellationToken, Task<TOutput>> handler,
        TInput input,
        CancellationToken cancellationToken
    ) => handler(input, cancellationToken);

    /// <summary>
    /// Serialized through the same path as a modeled error: every operation implicitly carries
    /// smithy.framework#ValidationException, and a protocol answers a framework fault itself. A
    /// protocol that cannot serialize server errors rethrows, surfaced as a 500 by the host.
    /// </summary>
    private static SmithyHttpServerResponse SerializeRequestFailure<TInput, TOutput>(
        IServerOperationProtocol<TInput, TOutput> protocol,
        Exception exception
    ) => protocol.TrySerializeError(exception, out var response) ? response : throw exception;
}
