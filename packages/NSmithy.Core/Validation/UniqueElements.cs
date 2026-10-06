using System.Globalization;
using System.Numerics;
using System.Text;
using NSmithy.Core.Serde;

namespace NSmithy.Core.Validation;

/// <summary>
/// Finds a duplicate among a list's elements by the model's equality rather than .NET's: each
/// element is rendered to a canonical key, so a blob compares by content and a structure holding a
/// list compares by that list's elements, not by reference.
/// </summary>
internal struct UniqueElements : IShapeSerializer
{
    private HashSet<string>? seen;

    public bool HasDuplicate { get; private set; }

    private void Add(Action<CanonicalSerializer> write)
    {
        var key = new StringBuilder();
        write(new CanonicalSerializer(key));
        seen ??= new HashSet<string>(StringComparer.Ordinal);
        if (!seen.Add(key.ToString()))
        {
            HasDuplicate = true;
        }
    }

    public void WriteNull(int member) => Add(c => c.WriteNull(member));

    public void WriteBoolean(int member, bool value) => Add(c => c.WriteBoolean(member, value));

    public void WriteByte(int member, sbyte value) => Add(c => c.WriteByte(member, value));

    public void WriteShort(int member, short value) => Add(c => c.WriteShort(member, value));

    public void WriteInteger(int member, int value) => Add(c => c.WriteInteger(member, value));

    public void WriteLong(int member, long value) => Add(c => c.WriteLong(member, value));

    public void WriteFloat(int member, float value) => Add(c => c.WriteFloat(member, value));

    public void WriteDouble(int member, double value) => Add(c => c.WriteDouble(member, value));

    public void WriteBigInteger(int member, BigInteger value) =>
        Add(c => c.WriteBigInteger(member, value));

    public void WriteBigDecimal(int member, decimal value) =>
        Add(c => c.WriteBigDecimal(member, value));

    public void WriteString(int member, string value) => Add(c => c.WriteString(member, value));

    public void WriteBlob(int member, byte[] value) => Add(c => c.WriteBlob(member, value));

    public void WriteTimestamp(int member, DateTimeOffset value) =>
        Add(c => c.WriteTimestamp(member, value));

    public void WriteDocument(int member, Document value) =>
        Add(c => c.WriteDocument(member, value));

    public void WriteStringEnum(int member, string value) =>
        Add(c => c.WriteStringEnum(member, value));

    public void WriteIntEnum(int member, int value) => Add(c => c.WriteIntEnum(member, value));

    public void WriteStream(int member, Stream value) => Add(c => c.WriteStream(member, value));

    public void WriteEventStream<TEvent>(
        int member,
        IAsyncEnumerable<TEvent> events,
        Schema<TEvent> eventSchema
    ) => Add(c => c.WriteEventStream(member, events, eventSchema));

    public void WriteStruct<T>(int member, T value, IStructSchema<T> schema) =>
        Add(c => c.WriteStruct(member, value, schema));

    public void WriteList<TCollection, TElement>(
        int member,
        TCollection value,
        IListSchema<TCollection, TElement> schema
    ) => Add(c => c.WriteList(member, value, schema));

    public void WriteMap<TDictionary, TValue>(
        int member,
        TDictionary value,
        IMapSchema<TDictionary, TValue> schema
    ) => Add(c => c.WriteMap(member, value, schema));

    public void WriteUnion<T>(int member, T value, IUnionSchema<T> schema) =>
        Add(c => c.WriteUnion(member, value, schema));
}

/// <summary>
/// Renders a value to a string that is equal for two values exactly when the model considers them
/// equal: every scalar is tagged and length-prefixed where it could be ambiguous, and a map's
/// entries are sorted so insertion order does not matter.
/// </summary>
internal struct CanonicalSerializer(StringBuilder key) : IShapeSerializer
{
    // Where each entry of the map being written begins, so the entries can be sorted afterwards.
    private readonly List<int>? entryStarts;

    private CanonicalSerializer(StringBuilder key, List<int> entryStarts)
        : this(key)
    {
        this.entryStarts = entryStarts;
    }

    private readonly StringBuilder Begin(int member, char tag) =>
        key.Append('#')
            .Append(member.ToString(CultureInfo.InvariantCulture))
            .Append('=')
            .Append(tag);

    private readonly void Text(int member, char tag, string value) =>
        Begin(member, tag)
            .Append(value.Length.ToString(CultureInfo.InvariantCulture))
            .Append(':')
            .Append(value)
            .Append(';');

    private readonly void Number(int member, char tag, IFormattable value, string? format = null) =>
        Begin(member, tag).Append(value.ToString(format, CultureInfo.InvariantCulture)).Append(';');

    public readonly void WriteNull(int member) => Begin(member, 'n').Append(';');

    public readonly void WriteBoolean(int member, bool value) =>
        Begin(member, 'b').Append(value ? '1' : '0').Append(';');

    public readonly void WriteByte(int member, sbyte value) => Number(member, 'i', value);

    public readonly void WriteShort(int member, short value) => Number(member, 'i', value);

    public readonly void WriteInteger(int member, int value) => Number(member, 'i', value);

    public readonly void WriteLong(int member, long value) => Number(member, 'i', value);

    public readonly void WriteFloat(int member, float value) => Number(member, 'f', value, "R");

    public readonly void WriteDouble(int member, double value) => Number(member, 'f', value, "R");

    public readonly void WriteBigInteger(int member, BigInteger value) =>
        Number(member, 'i', value);

    public readonly void WriteBigDecimal(int member, decimal value) => Number(member, 'm', value);

    public readonly void WriteString(int member, string value)
    {
        // A map entry begins with its key.
        if (entryStarts is not null && member == 0)
        {
            entryStarts.Add(key.Length);
        }

        Text(member, 's', value);
    }

    public readonly void WriteBlob(int member, byte[] value) =>
        Text(member, 'x', Convert.ToBase64String(value));

    public readonly void WriteTimestamp(int member, DateTimeOffset value) =>
        Number(member, 't', value.UtcTicks);

    public readonly void WriteDocument(int member, Document value)
    {
        Begin(member, 'j');
        AppendDocument(key, value);
        key.Append(';');
    }

    public readonly void WriteStringEnum(int member, string value) => Text(member, 'e', value);

    public readonly void WriteIntEnum(int member, int value) => Number(member, 'e', value);

    public readonly void WriteStream(int member, Stream value) =>
        throw new NotSupportedException("A streaming blob has no value to compare.");

    public readonly void WriteEventStream<TEvent>(
        int member,
        IAsyncEnumerable<TEvent> events,
        Schema<TEvent> eventSchema
    ) => throw new NotSupportedException("An event stream has no value to compare.");

    public readonly void WriteStruct<T>(int member, T value, IStructSchema<T> schema)
    {
        Begin(member, '{');
        var nested = new CanonicalSerializer(key);
        schema.SerializeMembers(value, ref nested);
        key.Append('}');
    }

    public readonly void WriteList<TCollection, TElement>(
        int member,
        TCollection value,
        IListSchema<TCollection, TElement> schema
    )
    {
        Begin(member, '[');
        var nested = new CanonicalSerializer(key);
        schema.SerializeElements(value, ref nested);
        key.Append(']');
    }

    public readonly void WriteMap<TDictionary, TValue>(
        int member,
        TDictionary value,
        IMapSchema<TDictionary, TValue> schema
    )
    {
        Begin(member, '(');
        var starts = new List<int>();
        var nested = new CanonicalSerializer(key, starts);
        schema.SerializeEntries(value, ref nested);
        if (starts.Count > 1)
        {
            var entries = new string[starts.Count];
            for (var index = 0; index < starts.Count; index++)
            {
                var end = index + 1 < starts.Count ? starts[index + 1] : key.Length;
                entries[index] = key.ToString(starts[index], end - starts[index]);
            }

            Array.Sort(entries, StringComparer.Ordinal);
            key.Length = starts[0];
            foreach (var entry in entries)
            {
                key.Append(entry);
            }
        }

        key.Append(')');
    }

    public readonly void WriteUnion<T>(int member, T value, IUnionSchema<T> schema)
    {
        Begin(member, '<');
        var nested = new CanonicalSerializer(key);
        schema.SerializeCase(value, ref nested);
        key.Append('>');
    }

    private static void AppendDocument(StringBuilder key, Document value)
    {
        switch (value.Kind)
        {
            case DocumentKind.Null:
                key.Append('n');
                break;
            case DocumentKind.Boolean:
                key.Append(value.AsBoolean() ? "b1" : "b0");
                break;
            case DocumentKind.Number:
                key.Append('m').Append(value.AsNumber().ToString(CultureInfo.InvariantCulture));
                break;
            case DocumentKind.String:
                var text = value.AsString();
                key.Append('s')
                    .Append(text.Length.ToString(CultureInfo.InvariantCulture))
                    .Append(':')
                    .Append(text);
                break;
            case DocumentKind.Array:
                key.Append('[');
                foreach (var item in value.AsArray())
                {
                    AppendDocument(key, item);
                    key.Append(',');
                }

                key.Append(']');
                break;
            case DocumentKind.Object:
                key.Append('{');
                foreach (
                    var (name, item) in value
                        .AsObject()
                        .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                )
                {
                    key.Append(name.Length.ToString(CultureInfo.InvariantCulture))
                        .Append(':')
                        .Append(name);
                    AppendDocument(key, item);
                    key.Append(',');
                }

                key.Append('}');
                break;
        }
    }
}
