using System.Formats.Cbor;
using NSmithy.Core.Serde;

namespace NSmithy.Codecs.Cbor;

/// <summary>
/// Writes a structure's members with no enclosing map, for a caller that adds entries of its own
/// (such as an error's <c>__type</c>). The plan is built once, when this is created.
/// </summary>
internal sealed class CborMembersWriter<T>(IStructSchema<T> schema)
{
    private readonly CborShapePlan plan = new CborPlans().ForTarget((Schema)schema)!;

    public void Write(CborWriter writer, T value) =>
        CborShapeSerializer.WriteMembers(writer, plan, materializeDefaults: true, schema, value);
}
