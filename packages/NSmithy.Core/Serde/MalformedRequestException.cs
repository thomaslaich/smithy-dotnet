namespace NSmithy.Core.Serde;

/// <summary>
/// What a server could not do with a request whose bytes never became modeled input. Each kind maps
/// to a different HTTP status, but the mapping belongs to the protocol, not here: this says what
/// went wrong, and the protocol says how that looks on the wire.
/// </summary>
public enum MalformedRequestKind
{
    /// <summary>The request could not be read as the shape the model declares.</summary>
    Serialization,

    /// <summary>The request's <c>Content-Type</c> is not the one the operation's input is bound to.</summary>
    UnsupportedMediaType,

    /// <summary>The request's <c>Accept</c> excludes the media type the operation's output uses.</summary>
    NotAcceptable,
}

/// <summary>
/// Thrown while a server reads a request that never becomes modeled input at all: a body that is not
/// valid JSON, a non-numeric integer, a timestamp in the wrong format, a content type the operation
/// does not accept. It is the counterpart of
/// <see cref="NSmithy.Core.Validation.ValidationException"/>, which answers input that parsed but
/// broke a constraint — and like it, a caller mistake rather than a server fault, so the runtime
/// answers with a structured 4xx instead of letting it reach the host as a 500.
/// </summary>
public sealed class MalformedRequestException(MalformedRequestKind kind, string message)
    : Exception(message)
{
    public MalformedRequestKind Kind { get; } = kind;

    public static MalformedRequestException Serialization(string message) =>
        new(MalformedRequestKind.Serialization, message);

    public static MalformedRequestException UnsupportedMediaType(string message) =>
        new(MalformedRequestKind.UnsupportedMediaType, message);

    public static MalformedRequestException NotAcceptable(string message) =>
        new(MalformedRequestKind.NotAcceptable, message);
}

/// <summary>
/// The body a <see cref="MalformedRequestException"/> serializes to. Smithy models no shape for
/// these faults — the status and the protocol's error discriminator are the whole contract the
/// malformed-request suite asserts — but a response a caller cannot read is a poor one, so the
/// runtime carries the same single <c>message</c> member every Smithy error has.
/// </summary>
public static class MalformedRequestSchema
{
    public static readonly ShapeId Id = ShapeId.Parse("smithy.framework#MalformedRequest");

    public sealed class Builder
    {
        public string? Message { get; set; }
    }

    public static Schema<MalformedRequestException> Schema { get; } =
        Schemas
            .Structure<MalformedRequestException, Builder>(
                Id,
                [new Trait(ShapeId.Parse("smithy.api#error"), Document.From("client"))]
            )
            .Required(
                "message",
                static value => value.Message,
                static (builder, value) => builder.Message = value,
                Schemas.NullableReference(Schemas.String),
                [FrameworkTraits.ProtoIndex(1)]
            )
            .Build(
                static () => new Builder(),
                static builder =>
                    MalformedRequestException.Serialization(
                        builder.Message ?? throw new MissingRequiredMemberException("message")
                    )
            );

    /// <summary>
    /// The wire name a protocol puts in its error discriminator, and the status that goes with it.
    /// These are the names AWS's REST protocols use and the malformed-request suite asserts.
    /// </summary>
    public static (string ErrorType, int StatusCode) Wire(MalformedRequestKind kind) =>
        kind switch
        {
            MalformedRequestKind.Serialization => ("SerializationException", 400),
            MalformedRequestKind.UnsupportedMediaType => ("UnsupportedMediaTypeException", 415),
            MalformedRequestKind.NotAcceptable => ("NotAcceptableException", 406),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
}

/// <summary>
/// Traits a framework shape declares for itself. A modeled shape gets its traits from a model file;
/// a shape the runtime owns has none, so anything a codec requires has to be stated here.
/// </summary>
internal static class FrameworkTraits
{
    private static readonly ShapeId ProtoIndexTrait = ShapeId.Parse("alloy.proto#protoIndex");

    /// <summary>
    /// Every protocol has to be able to put a framework shape on the wire, because the server
    /// runtime can return one from any operation. The proto codec requires a field number on every
    /// member, and unlike a modeled shape there is no model file to carry one.
    /// </summary>
    public static Trait ProtoIndex(int index) => new(ProtoIndexTrait, Document.From(index));
}
