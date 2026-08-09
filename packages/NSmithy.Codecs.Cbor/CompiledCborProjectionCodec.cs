using System.Formats.Cbor;
using System.Globalization;
using System.Numerics;
using NSmithy.Core;
using NSmithy.Core.Serde;
using static NSmithy.Codecs.Cbor.CborWire;

namespace NSmithy.Codecs.Cbor;

internal sealed class CompiledCborProjectionCodec<T, TBuilder>(
    StructProjection<T, TBuilder> projection,
    bool materializeTopLevelDefaults
) : IProjectionCodec<T, TBuilder>
{
    private readonly StructureCborValueWriter<T> valueWriter = CompileWriter(
        projection,
        materializeTopLevelDefaults
    );
    private readonly CborProjectionValueReader<TBuilder> valueReader = CompileReader(projection);

    public byte[] Serialize(T value)
    {
        var writer = new CborWriter(CborConformanceMode.Lax);
        valueWriter.Write(writer, value);
        return writer.Encode();
    }

    public void ReadInto(byte[] payload, TBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(builder);
        if (payload.Length == 0)
        {
            return;
        }

        var reader = new CborReader(payload, CborConformanceMode.Lax);
        valueReader.ReadInto(reader, builder);
    }

    private static StructureCborValueWriter<T> CompileWriter(
        StructProjection<T, TBuilder> projection,
        bool materializeTopLevelDefaults
    )
    {
        var visitor = new CborMemberWriterCompiler<T>(
            new CborWriterCompiler(materializeTopLevelDefaults),
            materializeTopLevelDefaults
        );
        projection.VisitMembers(visitor);
        return new StructureCborValueWriter<T>(visitor.Writers);
    }

    private static CborProjectionValueReader<TBuilder> CompileReader(
        StructProjection<T, TBuilder> projection
    )
    {
        var visitor = new CborProjectionMemberReaderCompiler<T, TBuilder>(new CborReaderCompiler());
        projection.VisitMembers(visitor);
        return new CborProjectionValueReader<TBuilder>(visitor.Readers);
    }
}

internal interface ICborProjectionMemberReader<in TBuilder>
{
    string Name { get; }

    bool IsRequired { get; }

    void ReadMissing(TBuilder builder);

    void ReadInto(TBuilder builder, CborReader reader);
}

internal sealed class CborProjectionMemberReaderCompiler<TContainer, TBuilder>(
    CborReaderCompiler compiler
) : IMemberVisitor<TContainer, TBuilder>
{
    private readonly List<ICborProjectionMemberReader<TBuilder>> readers = [];

    public IReadOnlyList<ICborProjectionMemberReader<TBuilder>> Readers => readers;

    public void Visit<TValue>(IMemberSchema<TContainer, TBuilder, TValue> member)
    {
        readers.Add(
            new CborProjectionMemberReader<TContainer, TBuilder, TValue>(
                member,
                compiler.CompileValue(member.TargetSchema, member.MemberTraits)
            )
        );
    }
}

internal sealed class CborProjectionMemberReader<TContainer, TBuilder, TValue>(
    IMemberSchema<TContainer, TBuilder, TValue> member,
    ICborValueReader<TValue> valueReader
) : ICborProjectionMemberReader<TBuilder>
{
    public string Name => member.Name;

    public bool IsRequired => member.IsRequired;

    public void ReadMissing(TBuilder builder)
    {
        if (TryCreateDefaultValue(member.TargetSchema, member.MemberTraits, out TValue? defaultValue))
        {
            member.SetValue(builder, defaultValue!);
        }
    }

    public void ReadInto(TBuilder builder, CborReader reader) =>
        member.SetValue(builder, valueReader.Read(reader));
}

internal sealed class CborProjectionValueReader<TBuilder>(
    IReadOnlyList<ICborProjectionMemberReader<TBuilder>> memberReaders
)
{
    private readonly Dictionary<string, ICborProjectionMemberReader<TBuilder>> readersByName =
        memberReaders.ToDictionary(reader => reader.Name, StringComparer.Ordinal);

    public void ReadInto(CborReader reader, TBuilder builder)
    {
        if (reader.PeekState() != CborReaderState.StartMap)
        {
            throw new InvalidOperationException("Expected CBOR map for structure projection.");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        reader.ReadStartMap();
        while (reader.PeekState() != CborReaderState.EndMap)
        {
            var name = reader.ReadTextString();
            if (readersByName.TryGetValue(name, out var memberReader))
            {
                if (reader.PeekState() == CborReaderState.Null && memberReader.IsRequired)
                {
                    reader.ReadNull();
                    throw new InvalidOperationException(
                        $"Required member '{name}' cannot be null."
                    );
                }

                seen.Add(name);
                memberReader.ReadInto(builder, reader);
            }
            else
            {
                reader.SkipValue();
            }
        }

        reader.ReadEndMap();
        foreach (var memberReader in memberReaders)
        {
            if (seen.Contains(memberReader.Name))
            {
                continue;
            }

            if (memberReader.IsRequired)
            {
                throw new InvalidOperationException(
                    $"Missing required member '{memberReader.Name}'."
                );
            }

            memberReader.ReadMissing(builder);
        }
    }
}
