using System.Globalization;
using System.Text.Json;
using NSmithy.Core;

namespace NSmithy.Codecs.Json;

internal static class DocumentJsonWriter
{
    /// <summary>Write a <see cref="Document"/> tree to <paramref name="writer"/> as raw JSON.</summary>
    public static void Write(Utf8JsonWriter writer, Document value)
    {
        switch (value.Kind)
        {
            case DocumentKind.Null:
                writer.WriteNullValue();
                break;
            case DocumentKind.Boolean:
                writer.WriteBooleanValue(value.AsBoolean());
                break;
            case DocumentKind.Number:
                writer.WriteNumberValue(value.AsNumber());
                break;
            case DocumentKind.String:
                writer.WriteStringValue(value.AsString());
                break;
            case DocumentKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.AsArray())
                {
                    Write(writer, item);
                }
                writer.WriteEndArray();
                break;
            case DocumentKind.Object:
                writer.WriteStartObject();
                foreach (var (k, v) in value.AsObject())
                {
                    writer.WritePropertyName(k);
                    Write(writer, v);
                }
                writer.WriteEndObject();
                break;
            default:
                throw new InvalidOperationException(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "Unsupported document kind '{0}'.",
                        value.Kind
                    )
                );
        }
    }
}
