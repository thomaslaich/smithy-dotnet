using System.Numerics;

namespace NSmithy.Core.Serde;

/// <summary>
/// An <see cref="ISchemaVisitor{TResult}"/> that handles a few shape kinds and answers every other
/// one through <see cref="VisitDefault"/>, which by default refuses it. For a consumer that only
/// admits, say, a map: override <see cref="VisitMap"/> and nothing else.
/// </summary>
public abstract class PartialSchemaVisitor<TResult> : ISchemaVisitor<TResult>
{
    protected virtual TResult VisitDefault(Schema schema) =>
        throw new NotSupportedException(
            $"{GetType().Name} does not support schema kind '{schema.Kind}'."
        );

    public virtual TResult VisitBoolean(Schema<bool> schema) => VisitDefault(schema);

    public virtual TResult VisitByte(Schema<sbyte> schema) => VisitDefault(schema);

    public virtual TResult VisitShort(Schema<short> schema) => VisitDefault(schema);

    public virtual TResult VisitInteger(Schema<int> schema) => VisitDefault(schema);

    public virtual TResult VisitLong(Schema<long> schema) => VisitDefault(schema);

    public virtual TResult VisitFloat(Schema<float> schema) => VisitDefault(schema);

    public virtual TResult VisitDouble(Schema<double> schema) => VisitDefault(schema);

    public virtual TResult VisitBigInteger(Schema<BigInteger> schema) => VisitDefault(schema);

    public virtual TResult VisitBigDecimal(Schema<decimal> schema) => VisitDefault(schema);

    public virtual TResult VisitString(Schema<string> schema) => VisitDefault(schema);

    public virtual TResult VisitBlob(Schema<byte[]> schema) => VisitDefault(schema);

    public virtual TResult VisitStreamingBlob(Schema<Stream> schema) => VisitDefault(schema);

    public virtual TResult VisitTimestamp(Schema<DateTimeOffset> schema) => VisitDefault(schema);

    public virtual TResult VisitDocument(Schema<Document> schema) => VisitDefault(schema);

    public virtual TResult VisitNullable<T>(NullableSchema<T> schema)
        where T : struct => VisitDefault(schema);

    public virtual TResult VisitEventStream<TEvent>(EventStreamSchema<TEvent> schema) =>
        VisitDefault(schema);

    public virtual TResult VisitList<TCollection, TElement, TBuilder>(
        IListSchema<TCollection, TElement, TBuilder> schema
    ) => VisitDefault((Schema)schema);

    public virtual TResult VisitMap<TDictionary, TValue, TBuilder>(
        IMapSchema<TDictionary, TValue, TBuilder> schema
    ) => VisitDefault((Schema)schema);

    public virtual TResult VisitStruct<T, TBuilder>(IStructSchema<T, TBuilder> schema) =>
        VisitDefault((Schema)schema);

    public virtual TResult VisitUnion<T>(IUnionSchema<T> schema) => VisitDefault((Schema)schema);

    public virtual TResult VisitStringEnum<T>(StringEnumSchema<T> schema)
        where T : IStringEnumValue<T> => VisitDefault(schema);

    public virtual TResult VisitIntEnum<T>(IntEnumSchema<T> schema)
        where T : struct, Enum => VisitDefault(schema);
}
