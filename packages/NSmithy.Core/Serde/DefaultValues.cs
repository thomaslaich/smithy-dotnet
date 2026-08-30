using System.Numerics;

namespace NSmithy.Core.Serde;

/// <summary>
/// Resolves a member's <c>@default</c> into a typed factory, once, at compile time.
/// </summary>
/// <remarks>
/// The result is a factory rather than a value because a default can be mutable (a blob, list, map
/// or document); a reader that sets it into a builder must not alias one instance across objects.
/// A writer that only compares against the default may call the factory once and keep the result.
/// </remarks>
public static class DefaultValues
{
    private static readonly ShapeId ClientOptionalTrait = new("smithy.api", "clientOptional");
    private static readonly ShapeId DefaultTrait = new("smithy.api", "default");

    /// <param name="honorClientOptional">
    /// Whether a member carrying <c>@clientOptional</c> is treated as having no default, which is
    /// what a body codec does so that a client does not fabricate a value the server never sent.
    /// </param>
    public static bool TryCompile<T>(
        Schema<T> schema,
        IReadOnlyDictionary<ShapeId, Trait> traits,
        bool honorClientOptional,
        out Func<T> create
    )
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(traits);

        if (
            (honorClientOptional && traits.ContainsKey(ClientOptionalTrait))
            || !traits.TryGetValue(DefaultTrait, out var trait)
            || trait.Value.Kind == DocumentKind.Null
        )
        {
            create = null!;
            return false;
        }

        if (schema.Resolved.Accept(new Compiler(trait.Value)) is Func<T> compiled)
        {
            create = compiled;
            return true;
        }

        create = null!;
        return false;
    }

    // Returns a Func<T> for the visited Schema<T>, or null when the shape kind has no default form.
    private sealed class Compiler(Document value) : ISchemaVisitor<object?>
    {
        public object? VisitBoolean(Schema<bool> schema) => Constant(value.AsBoolean());

        public object? VisitByte(Schema<sbyte> schema) => Constant((sbyte)value.AsNumber());

        public object? VisitShort(Schema<short> schema) => Constant((short)value.AsNumber());

        public object? VisitInteger(Schema<int> schema) => Constant((int)value.AsNumber());

        public object? VisitLong(Schema<long> schema) => Constant((long)value.AsNumber());

        public object? VisitFloat(Schema<float> schema) => Constant((float)value.AsNumber());

        public object? VisitDouble(Schema<double> schema) => Constant((double)value.AsNumber());

        public object? VisitBigInteger(Schema<BigInteger> schema) =>
            Constant(new BigInteger(value.AsNumber()));

        public object? VisitBigDecimal(Schema<decimal> schema) => Constant(value.AsNumber());

        public object? VisitString(Schema<string> schema) => Constant(value.AsString());

        public object? VisitBlob(Schema<byte[]> schema)
        {
            var text = value.AsString();
            return new Func<byte[]>(() => Convert.FromBase64String(text));
        }

        public object? VisitStreamingBlob(Schema<Stream> schema) => null;

        public object? VisitTimestamp(Schema<DateTimeOffset> schema) =>
            Constant(DateTimeOffset.FromUnixTimeSeconds((long)value.AsNumber()));

        public object? VisitDocument(Schema<Document> schema) => Constant(value);

        public object? VisitNullable<T>(NullableSchema<T> schema)
            where T : struct =>
            schema.TargetSchema.Resolved.Accept(this) is Func<T> inner
                ? new Func<T?>(() => inner())
                : null;

        public object? VisitEventStream<TEvent>(EventStreamSchema<TEvent> schema) => null;

        public object? VisitList<TCollection, TElement, TBuilder>(
            IListSchema<TCollection, TElement, TBuilder> schema
        )
        {
            var items = new List<Func<TElement>>();
            foreach (var item in value.AsArray())
            {
                if (
                    schema.ElementSchema.Resolved.Accept(new Compiler(item))
                    is not Func<TElement> element
                )
                {
                    return null;
                }

                items.Add(element);
            }

            var elements = items.ToArray();
            return new Func<TCollection>(() =>
            {
                var builder = schema.CreateTypedBuilder();
                foreach (var element in elements)
                {
                    schema.Add(builder, element());
                }

                return schema.Build(builder);
            });
        }

        public object? VisitMap<TDictionary, TValue, TBuilder>(
            IMapSchema<TDictionary, TValue, TBuilder> schema
        )
        {
            var entries = new List<KeyValuePair<string, Func<TValue>>>();
            foreach (var entry in value.AsObject())
            {
                if (
                    schema.ValueSchema.Resolved.Accept(new Compiler(entry.Value))
                    is not Func<TValue> entryValue
                )
                {
                    return null;
                }

                entries.Add(new(entry.Key, entryValue));
            }

            var values = entries.ToArray();
            return new Func<TDictionary>(() =>
            {
                var builder = schema.CreateTypedBuilder();
                foreach (var entry in values)
                {
                    schema.Add(builder, entry.Key, entry.Value());
                }

                return schema.Build(builder);
            });
        }

        public object? VisitStruct<T, TBuilder>(IStructSchema<T, TBuilder> schema) => null;

        public object? VisitUnion<T>(IUnionSchema<T> schema) => null;

        public object? VisitStringEnum<T>(StringEnumSchema<T> schema)
            where T : IStringEnumValue<T> => Constant(schema.Create(value.AsString()));

        public object? VisitIntEnum<T>(IntEnumSchema<T> schema)
            where T : struct, Enum => Constant(schema.Create((int)value.AsNumber()));

        private static Func<T> Constant<T>(T constant) => () => constant;
    }
}
