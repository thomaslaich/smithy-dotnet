using NSmithy.Core.Serde;

namespace NSmithy.Core.Validation;

/// <summary>
/// The modeled <c>smithy.framework#ValidationException</c> error a server returns when a request's
/// deserialized input violates the model's constraint traits. Every operation carries it as an
/// implicit modeled error (see <see cref="OperationSchema{TInput, TOutput}"/>), so protocols
/// serialize it like any other modeled error and generated clients can deserialize it.
/// </summary>
public sealed class ValidationException : Exception
{
    public ValidationException(
        string? message,
        IReadOnlyList<ValidationExceptionField>? fieldList = null
    )
        : base(message ?? "Validation failed.")
    {
        FieldList = fieldList ?? [];
    }

    public IReadOnlyList<ValidationExceptionField> FieldList { get; }

    public static ValidationException FromErrors(IReadOnlyList<SmithyValidationError> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        var message =
            errors.Count == 1
                ? errors[0].Message
                : FormattableString.Invariant($"{errors.Count} validation errors detected.");
        return new ValidationException(
            message,
            [.. errors.Select(error => new ValidationExceptionField(error.Path, error.Message))]
        );
    }

    /// <summary>
    /// The codec-level counterpart of a <c>@required</c> violation: a member missing from the
    /// payload never reaches the compiled validator, because deserialization fails first.
    /// </summary>
    public static ValidationException FromMissingRequiredMember(
        MissingRequiredMemberException exception
    )
    {
        ArgumentNullException.ThrowIfNull(exception);
        var path = JsonPointer.Root;
        foreach (var token in exception.PathTokens)
        {
            path = JsonPointer.Append(path, token);
        }

        var message = $"Required member '{exception.MemberName}' must not be null.";
        return new ValidationException(message, [new ValidationExceptionField(path, message)]);
    }
}

public sealed record ValidationExceptionField(string Path, string Message);

public static class ValidationExceptionFieldSchema
{
    public sealed class Builder
    {
        public string? Path { get; set; }

        public string? Message { get; set; }
    }

    public static Schema<ValidationExceptionField> Schema { get; } =
        Schemas
            .Structure<ValidationExceptionField, Builder>(
                ShapeId.Parse("smithy.framework#ValidationExceptionField")
            )
            .Required(
                "path",
                static value => value.Path,
                static (builder, value) => builder.Path = value,
                Schemas.NullableReference(Schemas.String),
                [FrameworkTraits.ProtoIndex(1)]
            )
            .Required(
                "message",
                static value => value.Message,
                static (builder, value) => builder.Message = value,
                Schemas.NullableReference(Schemas.String),
                [FrameworkTraits.ProtoIndex(2)]
            )
            .Build(
                static () => new Builder(),
                static builder => new ValidationExceptionField(
                    builder.Path ?? throw new MissingRequiredMemberException("path"),
                    builder.Message ?? throw new MissingRequiredMemberException("message")
                )
            );
}

internal static class FrameworkTraits
{
    private static readonly ShapeId ProtoIndexTrait = ShapeId.Parse("alloy.proto#protoIndex");

    /// <summary>
    /// Every protocol has to be able to put this shape on the wire, because the server runtime can
    /// return it from any operation. The proto codec requires a field number on every member, and
    /// unlike a modeled shape there is no model file to carry one, so the framework shape declares
    /// its own.
    /// </summary>
    public static Trait ProtoIndex(int index) => new(ProtoIndexTrait, Document.From(index));
}

public static class ValidationExceptionSchema
{
    public static readonly ShapeId Id = ShapeId.Parse("smithy.framework#ValidationException");

    public sealed class Builder
    {
        public string? Message { get; set; }

        public IReadOnlyList<ValidationExceptionField>? FieldList { get; set; }
    }

    public static Schema<ValidationException> Schema { get; } =
        Schemas
            .Structure<ValidationException, Builder>(
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
            .Optional(
                "fieldList",
                static value => value.FieldList,
                static (builder, value) => builder.FieldList = value,
                Schemas.NullableReference(
                    Schemas.List(
                        ShapeId.Parse("smithy.framework#ValidationExceptionFieldList"),
                        ValidationExceptionFieldSchema.Schema
                    )
                ),
                [FrameworkTraits.ProtoIndex(2)]
            )
            .Build(
                static () => new Builder(),
                static builder => new ValidationException(builder.Message, builder.FieldList)
            );

    /// <summary>
    /// The operation-error registration appended to every <see cref="OperationSchema{TInput,
    /// TOutput}"/> that does not model <c>smithy.framework#ValidationException</c> itself.
    /// </summary>
    public static IOperationErrorSchema OperationError { get; } =
        new OperationErrorSchema<ValidationException>(Id, Schema, 400);
}
