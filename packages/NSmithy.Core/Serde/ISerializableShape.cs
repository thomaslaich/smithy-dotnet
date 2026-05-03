namespace NSmithy.Core.Serde;

/// <summary>
/// A shape that knows how to serialize itself via an <see cref="IShapeSerializer"/>.
/// Implemented by every generated Smithy shape.
/// </summary>
public interface ISerializableShape
{
    /// <summary>The schema describing this shape's id, kind, traits, and members.</summary>
    Schema Schema { get; }

    /// <summary>Write the shape's data model to <paramref name="serializer"/>.</summary>
    void Serialize(IShapeSerializer serializer);
}

/// <summary>
/// A structure-shaped <see cref="ISerializableShape"/>. The <see cref="SerializeMembers"/> hook
/// lets codecs that need to inject framing (a struct opener, member separators, etc.) call into
/// the structure's member-writing logic without having to re-implement <see cref="ISerializableShape.Serialize(IShapeSerializer)"/>.
/// </summary>
public interface ISerializableStruct : ISerializableShape
{
    void SerializeMembers(IShapeSerializer serializer);
}
