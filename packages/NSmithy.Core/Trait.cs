namespace NSmithy.Core;

/// <summary>
/// A Smithy trait applied to a shape, identified by its shape id and carrying an optional
/// structured value as a <see cref="Document"/>. Codecs interpret the value per the trait's
/// declared schema (e.g. <c>smithy.api#xmlName</c> reads a string, <c>smithy.api#httpResponseCode</c>
/// reads a number).
/// </summary>
public readonly record struct Trait(ShapeId Id, Document Value)
{
    public Trait(ShapeId id)
        : this(id, Document.Null) { }

    public bool HasValue => Value.Kind != DocumentKind.Null;
}
