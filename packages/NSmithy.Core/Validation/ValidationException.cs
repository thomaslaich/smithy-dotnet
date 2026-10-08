using NSmithy.Core.Serde;

namespace NSmithy.Core.Validation;

/// <summary>
/// The modeled <c>smithy.framework#ValidationException</c> error a server returns when a request's
/// deserialized input violates the model's constraint traits. Every operation carries it as an
/// implicit modeled error (see <see cref="OperationSchema{TInput, TOutput}"/>), so protocols
/// serialize it like any other modeled error and generated clients can deserialize it.
/// </summary>
public sealed class ValidationException(
    string? message,
    IReadOnlyList<ValidationExceptionField>? fieldList = null
) : Exception(message ?? "Validation failed.")
{
    public IReadOnlyList<ValidationExceptionField> FieldList { get; } = fieldList ?? [];

    public static ValidationException FromErrors(IReadOnlyList<SmithyValidationError> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        // "N validation error(s) detected. <messages>" — the wording Smithy's malformed-request
        // tests assert, so a caller sees the same shape of message from any Smithy server.
        var detail = string.Join("; ", errors.Select(error => error.Message));
        var message = FormattableString.Invariant(
            $"{errors.Count} validation error{(errors.Count == 1 ? "" : "s")} detected. {detail}"
        );
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

        var message = $"Value at '{path}' failed to satisfy constraint: Member must not be null";
        return FromErrors([
            new SmithyValidationError(
                path,
                ValidationExceptionSchema.Id,
                ShapeId.Parse("smithy.api#required"),
                message
            ),
        ]);
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

    public static Schema<ValidationExceptionField> Schema { get; } = new StructureSchema();

    private sealed class StructureSchema()
        : StructSchema<ValidationExceptionField, Builder>(
            ShapeId.Parse("smithy.framework#ValidationExceptionField"),
            [new("path", Target0, isRequired: true), new("message", Target1, isRequired: true)]
        )
    {
        private static readonly Schema<string?> Target0 = Schemas.NullableReference(Schemas.String);
        private static readonly Schema<string?> Target1 = Schemas.NullableReference(Schemas.String);

        public override Builder CreateTypedBuilder() => new();

        public override ValidationExceptionField Build(Builder builder) =>
            new(
                builder.Path ?? throw new MissingRequiredMemberException("path"),
                builder.Message ?? throw new MissingRequiredMemberException("message")
            );

        public override void SerializeMembers<TSerializer>(
            ValidationExceptionField value,
            ref TSerializer serializer
        )
        {
            Target0.Write(0, value.Path, ref serializer);
            Target1.Write(1, value.Message, ref serializer);
        }

        public override void DeserializeMember<TDeserializer>(
            Builder builder,
            int index,
            ref TDeserializer deserializer
        )
        {
            switch (index)
            {
                case 0:
                    builder.Path = Target0.Read(ref deserializer);
                    break;
                case 1:
                    builder.Message = Target1.Read(ref deserializer);
                    break;
            }
        }
    }
}

public static class ValidationExceptionSchema
{
    public static readonly ShapeId Id = ShapeId.Parse("smithy.framework#ValidationException");

    public sealed class Builder
    {
        public string? Message { get; set; }

        public IReadOnlyList<ValidationExceptionField>? FieldList { get; set; }
    }

    public static Schema<ValidationException> Schema { get; } = new StructureSchema();

    private sealed class StructureSchema()
        : StructSchema<ValidationException, Builder>(
            ValidationExceptionSchema.Id,
            [new("message", Target0, isRequired: true), new("fieldList", Target1)],
            [new Trait(ShapeId.Parse("smithy.api#error"), Document.From("client"))]
        )
    {
        private static readonly Schema<string?> Target0 = Schemas.NullableReference(Schemas.String);
        private static readonly Schema<IReadOnlyList<ValidationExceptionField>?> Target1 =
            Schemas.NullableReference(
                Schemas.List(
                    ShapeId.Parse("smithy.framework#ValidationExceptionFieldList"),
                    ValidationExceptionFieldSchema.Schema
                )
            );

        public override Builder CreateTypedBuilder() => new();

        public override ValidationException Build(Builder builder) =>
            new(builder.Message, builder.FieldList);

        public override void SerializeMembers<TSerializer>(
            ValidationException value,
            ref TSerializer serializer
        )
        {
            Target0.Write(0, value.Message, ref serializer);
            Target1.Write(1, value.FieldList, ref serializer);
        }

        public override void DeserializeMember<TDeserializer>(
            Builder builder,
            int index,
            ref TDeserializer deserializer
        )
        {
            switch (index)
            {
                case 0:
                    builder.Message = Target0.Read(ref deserializer);
                    break;
                case 1:
                    builder.FieldList = Target1.Read(ref deserializer);
                    break;
            }
        }
    }

    /// <summary>
    /// The operation-error registration appended to every <see cref="OperationSchema{TInput,
    /// TOutput}"/> that does not model <c>smithy.framework#ValidationException</c> itself.
    /// </summary>
    public static IOperationErrorSchema OperationError { get; } =
        new OperationErrorSchema<ValidationException>(Id, Schema, 400);
}
