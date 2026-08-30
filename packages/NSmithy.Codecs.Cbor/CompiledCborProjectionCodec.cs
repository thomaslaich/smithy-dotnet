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
    private readonly ICborValueWriter<T> valueWriter = CompileWriter(
        projection,
        materializeTopLevelDefaults
    );
    private readonly CborProjectionValueReader<TBuilder> valueReader = CompileReader(projection);

    public byte[] Serialize(T value)
    {
        var writer = CborWriterCache.Rent();
        try
        {
            valueWriter.Write(writer, value);
            return writer.Encode();
        }
        finally
        {
            CborWriterCache.Return(writer);
        }
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

    private static ICborValueWriter<T> CompileWriter(
        StructProjection<T, TBuilder> projection,
        bool materializeTopLevelDefaults
    )
    {
        if (projection.Source.ValueSerializer is not { } valueSerializer)
        {
            var fallback = new CborMemberWriterCompiler<T>(
                new CborWriterCompiler(),
                materializeTopLevelDefaults
            );
            projection.VisitMembers(fallback);
            return new FallbackStructureCborValueWriter<T>(fallback.Writers);
        }

        var included = new CborMemberCollector<T>();
        projection.VisitMembers(included);
        var visitor = new CborMemberWriterCompiler<T>(
            new CborWriterCompiler(),
            materializeTopLevelDefaults,
            included.Members
        );
        projection.Source.VisitMembers(visitor);
        return new DirectStructureCborValueWriter<T>(valueSerializer, visitor.Plans);
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

    public ICborProjectionMemberReader<TBuilder>[] Readers => [.. readers];

    public void Visit<TValue>(IMemberSchema<TContainer, TBuilder, TValue> member)
    {
        readers.Add(
            new CborProjectionMemberReader<TContainer, TBuilder, TValue>(
                member,
                compiler.CompileValue(member.TargetSchema)
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

    // Constant per member, so resolved at compile time rather than per missing member.
    private readonly Func<TValue>? defaultValue = CompileDefault(
        member.TargetSchema,
        member.MemberTraits
    );

    public void ReadMissing(TBuilder builder)
    {
        if (defaultValue is { } create)
        {
            member.SetValue(builder, create());
        }
    }

    public void ReadInto(TBuilder builder, CborReader reader) =>
        member.SetValue(builder, valueReader.Read(reader));
}

internal sealed class CborProjectionValueReader<TBuilder>(
    ICborProjectionMemberReader<TBuilder>[] memberReaders
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
                    throw new MissingRequiredMemberException(name);
                }

                seen.Add(name);
                try
                {
                    memberReader.ReadInto(builder, reader);
                }
                catch (MissingRequiredMemberException exception)
                {
                    exception.PrependPathToken(memberReader.Name);
                    throw;
                }
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
                throw new MissingRequiredMemberException(memberReader.Name);
            }

            memberReader.ReadMissing(builder);
        }
    }
}
