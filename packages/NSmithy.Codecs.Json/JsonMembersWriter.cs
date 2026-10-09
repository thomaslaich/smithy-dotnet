using System.Text.Json;
using NSmithy.Core.Serde;

namespace NSmithy.Codecs.Json;

/// <summary>
/// Writes a structure's members with no enclosing object, for a caller that adds properties of its
/// own (such as an error's <c>__type</c>). The plan is built once, when this is created.
/// </summary>
internal sealed class JsonMembersWriter<T>(IStructSchema<T> schema, JsonCodecFactory factory)
{
    private readonly JsonShapePlan plan = factory.CreatePlans().ForTarget((Schema)schema)!;

    public void Write(Utf8JsonWriter writer, T value) =>
        JsonShapeSerializer.WriteMembers(writer, plan, materializeDefaults: true, schema, value);
}
