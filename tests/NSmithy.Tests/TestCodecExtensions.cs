using System.Text;
using NSmithy.Core.Functional;

namespace NSmithy.Tests;

/// <summary>
/// Test conveniences for the byte-oriented functional codecs. The wire is always bytes;
/// these UTF-8 helpers give a readable string view for assertions on text formats (JSON, XML).
/// </summary>
internal static class TestCodecExtensions
{
    public static string SerializeText<T>(this IFunctionalCodec<T> codec, T value) =>
        Encoding.UTF8.GetString(codec.Serialize(value));

    public static T DeserializeText<T>(this IFunctionalCodec<T> codec, string payload) =>
        codec.Deserialize(Encoding.UTF8.GetBytes(payload));
}
