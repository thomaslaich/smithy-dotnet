using System.Formats.Cbor;
using System.Numerics;

namespace RpcV2Cbor.Conformance;

/// <summary>
/// Structural CBOR comparison that normalises definite/indefinite length differences,
/// half/single/double float widths, and bignum tag vs plain integer.
/// </summary>
internal static class CborAssert
{
    public static void AreStructurallyEqual(byte[] expected, byte[] actual)
    {
        var exp = CborValue.Read(expected);
        var act = CborValue.Read(actual);
        var diff = FindFirstDiff(exp, act, "$");
        if (diff != null)
        {
            Assert.Fail($"CBOR mismatch at {diff}.\nExpected: {exp}\nActual:   {act}");
        }
    }

    /// <summary>Returns the JSON-path of the first difference, or null if equivalent.</summary>
    private static string? FindFirstDiff(CborValue exp, CborValue act, string path)
    {
        if (exp is CborMap em && act is CborMap am)
        {
            if (em.Entries.Count != am.Entries.Count)
                return $"{path}[mapCount: exp={em.Entries.Count} act={am.Entries.Count}]";
            var aDict = am.Entries.ToDictionary(e => e.Key, e => e.Value, StringComparer.Ordinal);
            foreach (var (k, v) in em.Entries)
            {
                if (!aDict.TryGetValue(k, out var av))
                    return $"{path}.{k}[missing in actual]";
                var d = FindFirstDiff(v, av, $"{path}.{k}");
                if (d != null)
                    return d;
            }
            return null;
        }
        if (exp is CborArray ea && act is CborArray aa)
        {
            if (ea.Items.Count != aa.Items.Count)
                return $"{path}[arrayLen: exp={ea.Items.Count} act={aa.Items.Count}]";
            for (var i = 0; i < ea.Items.Count; i++)
            {
                var d = FindFirstDiff(ea.Items[i], aa.Items[i], $"{path}[{i}]");
                if (d != null)
                    return d;
            }
            return null;
        }
        return exp.Equivalent(act) ? null : $"{path}[exp={exp} act={act}]";
    }
}

internal abstract class CborValue
{
    public static CborValue Read(byte[] bytes)
    {
        if (bytes.Length == 0)
            return new CborNull();
        var reader = new CborReader(bytes, CborConformanceMode.Lax);
        return ReadValue(ref reader);
    }

    public abstract bool Equivalent(CborValue other);

    private static CborValue ReadValue(ref CborReader reader)
    {
        var state = reader.PeekState();
        if (state == CborReaderState.Tag)
        {
            var tag = reader.ReadTag();
            var inner = ReadValue(ref reader);
            return tag switch
            {
                CborTag.UnixTimeSeconds => inner, // timestamp tag – treat inner as-is
                CborTag.UnsignedBigNum when inner is CborBytes b =>
                    new CborInt(new BigInteger(b.Value, isUnsigned: true, isBigEndian: true)),
                CborTag.NegativeBigNum when inner is CborBytes b =>
                    new CborInt(-1 - new BigInteger(b.Value, isUnsigned: true, isBigEndian: true)),
                _ => inner,
            };
        }

        return state switch
        {
            CborReaderState.Null => ReadNull(ref reader),
            CborReaderState.Boolean => new CborBool(reader.ReadBoolean()),
            CborReaderState.UnsignedInteger => new CborInt(new BigInteger(reader.ReadUInt64())),
            CborReaderState.NegativeInteger => new CborInt(new BigInteger(reader.ReadInt64())),
            CborReaderState.SinglePrecisionFloat => new CborFloat((double)reader.ReadSingle()),
            CborReaderState.DoublePrecisionFloat => new CborFloat(reader.ReadDouble()),
            CborReaderState.HalfPrecisionFloat => new CborFloat((double)reader.ReadHalf()),
            CborReaderState.TextString => new CborText(reader.ReadTextString()),
            CborReaderState.ByteString => new CborBytes(reader.ReadByteString()),
            CborReaderState.StartArray => ReadArray(ref reader),
            CborReaderState.StartMap => ReadMap(ref reader),
            _ => ReadSkipped(ref reader),
        };
    }

    private static CborNull ReadNull(ref CborReader reader)
    {
        reader.ReadNull();
        return new CborNull();
    }

    private static CborNull ReadSkipped(ref CborReader reader)
    {
        reader.SkipValue();
        return new CborNull();
    }

    private static CborArray ReadArray(ref CborReader reader)
    {
        reader.ReadStartArray();
        var items = new List<CborValue>();
        while (reader.PeekState() != CborReaderState.EndArray)
            items.Add(ReadValue(ref reader));
        reader.ReadEndArray();
        return new CborArray(items);
    }

    private static CborMap ReadMap(ref CborReader reader)
    {
        reader.ReadStartMap();
        var entries = new List<(string, CborValue)>();
        while (reader.PeekState() != CborReaderState.EndMap)
        {
            var key = reader.ReadTextString();
            var value = ReadValue(ref reader);
            entries.Add((key, value));
        }
        reader.ReadEndMap();
        return new CborMap(entries);
    }
}

internal sealed class CborNull : CborValue
{
    public override bool Equivalent(CborValue other) => other is CborNull;
    public override string ToString() => "null";
}

internal sealed class CborBool(bool value) : CborValue
{
    public bool Value { get; } = value;
    public override bool Equivalent(CborValue other) => other is CborBool b && Value == b.Value;
    public override string ToString() => Value ? "true" : "false";
}

internal sealed class CborInt(BigInteger value) : CborValue
{
    public BigInteger Value { get; } = value;

    public override bool Equivalent(CborValue other)
    {
        if (other is CborInt i)
            return Value == i.Value;
        // Allow int/float comparison (e.g. timestamp as int vs float).
        if (other is CborFloat f)
            return (double)Value == f.Value;
        return false;
    }

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

internal sealed class CborFloat(double value) : CborValue
{
    public double Value { get; } = value;

    public override bool Equivalent(CborValue other)
    {
        if (other is CborFloat f)
            return (double.IsNaN(Value) && double.IsNaN(f.Value)) || Value == f.Value;
        // Allow float/int comparison for timestamps and other cases where the spec
        // allows either encoding (tag 1 may carry integer or double).
        if (other is CborInt i)
            return Value == (double)i.Value;
        return false;
    }

    public override string ToString() => Value.ToString("G17", System.Globalization.CultureInfo.InvariantCulture);
}

internal sealed class CborText(string value) : CborValue
{
    public string Value { get; } = value;
    public override bool Equivalent(CborValue other) => other is CborText t && Value == t.Value;
    public override string ToString() => $"\"{Value}\"";
}

internal sealed class CborBytes(byte[] value) : CborValue
{
    public byte[] Value { get; } = value;

    public override bool Equivalent(CborValue other) =>
        other is CborBytes b && Value.SequenceEqual(b.Value);

    public override string ToString() => Convert.ToHexString(Value).ToLowerInvariant();
}

internal sealed class CborArray(IReadOnlyList<CborValue> items) : CborValue
{
    public IReadOnlyList<CborValue> Items { get; } = items;

    public override bool Equivalent(CborValue other) =>
        other is CborArray a
        && Items.Count == a.Items.Count
        && Items.Zip(a.Items).All(p => p.First.Equivalent(p.Second));

    public override string ToString() => $"[{string.Join(", ", Items)}]";
}

internal sealed class CborMap(IReadOnlyList<(string Key, CborValue Value)> entries) : CborValue
{
    public IReadOnlyList<(string Key, CborValue Value)> Entries { get; } = entries;

    public override bool Equivalent(CborValue other)
    {
        if (other is not CborMap m)
            return false;
        if (Entries.Count != m.Entries.Count)
            return false;
        var dict = m.Entries.ToDictionary(e => e.Key, e => e.Value, StringComparer.Ordinal);
        foreach (var (k, v) in Entries)
        {
            if (!dict.TryGetValue(k, out var ov) || !v.Equivalent(ov))
                return false;
        }
        return true;
    }

    public override string ToString() =>
        "{" + string.Join(", ", Entries.Select(e => $"\"{e.Key}\": {e.Value}")) + "}";
}
