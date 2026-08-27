using System.Numerics;
using System.Xml.Linq;
using NSmithy.Core;
using NSmithy.Core.Serde;
using static NSmithy.Codecs.Xml.XmlWire;

namespace NSmithy.Codecs.Xml;

internal interface IXmlValueReader<T>
{
    T Read(XElement? element);
}

internal interface IXmlMemberReader<in TBuilder>
{
    string Name { get; }

    bool IsRequired { get; }

    void ReadInto(TBuilder builder, XElement element);
}

internal interface IXmlUnionCaseReader<out TUnion>
{
    string Name { get; }

    TUnion Read(XElement element);
}

internal sealed class XmlReaderCompiler : ISchemaVisitor<object>
{
    private static readonly IReadOnlyDictionary<ShapeId, Trait> EmptyTraits =
        new Dictionary<ShapeId, Trait>();

    private readonly SchemaCompilationCache cache = new();

    public static IXmlValueReader<T> Compile<T>(
        Schema<T> schema,
        IReadOnlyDictionary<ShapeId, Trait>? memberTraits = null
    )
    {
        ArgumentNullException.ThrowIfNull(schema);
        var compiler = new XmlReaderCompiler();
        return memberTraits is null
            ? compiler.CompileValue(schema)
            : compiler.CompileValue(schema, memberTraits);
    }

    public static StructureXmlProjectionReader<TBuilder> Compile<T, TBuilder>(
        StructProjection<T, TBuilder> projection
    )
    {
        ArgumentNullException.ThrowIfNull(projection);
        var compiler = new XmlReaderCompiler();
        var visitor = new XmlMemberReaderCompiler<T, TBuilder>(compiler);
        projection.VisitMembers(visitor);
        return new StructureXmlProjectionReader<TBuilder>(visitor.Readers);
    }

    public IXmlValueReader<T> CompileValue<T>(Schema<T> schema) =>
        CompileValue(schema, EmptyTraits);

    public IXmlValueReader<T> CompileValue<T>(
        Schema<T> schema,
        IReadOnlyDictionary<ShapeId, Trait> memberTraits
    )
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(memberTraits);

        var resolved = schema.Resolved;
        if (memberTraits.Count != 0)
        {
            return (IXmlValueReader<T>)
                resolved.Accept(new MemberTraitXmlReaderCompiler(this, memberTraits));
        }

        return cache.GetOrCompile<IXmlValueReader<T>, DeferredXmlValueReader<T>>(
            resolved,
            static () => new DeferredXmlValueReader<T>(),
            target => (IXmlValueReader<T>)target.Accept(this)
        );
    }

    public object VisitBoolean(Schema<bool> schema) => Scalar(schema, EmptyTraits);

    public object VisitByte(Schema<sbyte> schema) => Scalar(schema, EmptyTraits);

    public object VisitShort(Schema<short> schema) => Scalar(schema, EmptyTraits);

    public object VisitInteger(Schema<int> schema) => Scalar(schema, EmptyTraits);

    public object VisitLong(Schema<long> schema) => Scalar(schema, EmptyTraits);

    public object VisitFloat(Schema<float> schema) => Scalar(schema, EmptyTraits);

    public object VisitDouble(Schema<double> schema) => Scalar(schema, EmptyTraits);

    public object VisitBigInteger(Schema<BigInteger> schema) => Scalar(schema, EmptyTraits);

    public object VisitBigDecimal(Schema<decimal> schema) => Scalar(schema, EmptyTraits);

    public object VisitString(Schema<string> schema) => Scalar(schema, EmptyTraits);

    public object VisitBlob(Schema<byte[]> schema) => Scalar(schema, EmptyTraits);

    public object VisitTimestamp(Schema<DateTimeOffset> schema) => Scalar(schema, EmptyTraits);

    public object VisitDocument(Schema<Document> schema) =>
        throw new NotSupportedException("Smithy Document values are not supported in XML.");

    public object VisitNullable<T>(NullableSchema<T> schema)
        where T : struct => new NullableXmlValueReader<T>(CompileValue(schema.TargetSchema));

    public object VisitStreamingBlob(Schema<Stream> schema) =>
        throw new NotSupportedException("XML codec does not support streaming blob schemas.");

    public object VisitEventStream<TEvent>(EventStreamSchema<TEvent> schema) =>
        throw new NotSupportedException("XML codec does not support event stream schemas.");

    public object VisitList<TCollection, TElement, TBuilder>(
        IListSchema<TCollection, TElement, TBuilder> schema
    ) =>
        new ListXmlValueReader<TCollection, TElement, TBuilder>(
            schema,
            CompileValue(
                schema.TypedElementMember.TargetSchema,
                schema.TypedElementMember.MemberTraits
            )
        );

    public object VisitMap<TDictionary, TValue, TBuilder>(
        IMapSchema<TDictionary, TValue, TBuilder> schema
    ) =>
        new MapXmlValueReader<TDictionary, TValue, TBuilder>(
            schema,
            CompileValue(schema.TypedValueMember.TargetSchema, schema.TypedValueMember.MemberTraits)
        );

    public object VisitStruct<T, TBuilder>(IStructSchema<T, TBuilder> schema)
    {
        var visitor = new XmlMemberReaderCompiler<T, TBuilder>(this);
        schema.VisitMembers(visitor);
        return new StructureXmlValueReader<T, TBuilder>(
            schema.CreateTypedBuilder,
            schema.Build,
            visitor.Readers
        );
    }

    public object VisitUnion<T>(IUnionSchema<T> schema)
    {
        var visitor = new XmlUnionCaseReaderCompiler<T>(this);
        schema.VisitCases(visitor);
        return new UnionXmlValueReader<T>(visitor.Readers);
    }

    public object VisitStringEnum<T>(StringEnumSchema<T> schema)
        where T : IStringEnumValue<T> => Scalar(schema, EmptyTraits);

    public object VisitIntEnum<T>(IntEnumSchema<T> schema)
        where T : struct, Enum => Scalar(schema, EmptyTraits);

    private static ScalarXmlValueReader<T> Scalar<T>(
        Schema<T> schema,
        IReadOnlyDictionary<ShapeId, Trait> traits
    ) => new(schema, traits);
}

internal sealed class DeferredXmlValueReader<T>
    : IXmlValueReader<T>,
        IDeferredCompilation<IXmlValueReader<T>>
{
    public void Complete(IXmlValueReader<T> compiled) => Set(compiled);

    private IXmlValueReader<T>? inner;

    public void Set(IXmlValueReader<T> reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        inner = reader;
    }

    public T Read(XElement? element)
    {
        if (inner is null)
        {
            throw new InvalidOperationException("XML reader has not been initialized.");
        }

        return inner.Read(element);
    }
}

internal sealed class MemberTraitXmlReaderCompiler(
    XmlReaderCompiler inner,
    IReadOnlyDictionary<ShapeId, Trait> memberTraits
) : ISchemaVisitor<object>
{
    public object VisitBoolean(Schema<bool> schema) =>
        new ScalarXmlValueReader<bool>(schema, memberTraits);

    public object VisitByte(Schema<sbyte> schema) =>
        new ScalarXmlValueReader<sbyte>(schema, memberTraits);

    public object VisitShort(Schema<short> schema) =>
        new ScalarXmlValueReader<short>(schema, memberTraits);

    public object VisitInteger(Schema<int> schema) =>
        new ScalarXmlValueReader<int>(schema, memberTraits);

    public object VisitLong(Schema<long> schema) =>
        new ScalarXmlValueReader<long>(schema, memberTraits);

    public object VisitFloat(Schema<float> schema) =>
        new ScalarXmlValueReader<float>(schema, memberTraits);

    public object VisitDouble(Schema<double> schema) =>
        new ScalarXmlValueReader<double>(schema, memberTraits);

    public object VisitBigInteger(Schema<BigInteger> schema) =>
        new ScalarXmlValueReader<BigInteger>(schema, memberTraits);

    public object VisitBigDecimal(Schema<decimal> schema) =>
        new ScalarXmlValueReader<decimal>(schema, memberTraits);

    public object VisitString(Schema<string> schema) =>
        new ScalarXmlValueReader<string>(schema, memberTraits);

    public object VisitBlob(Schema<byte[]> schema) =>
        new ScalarXmlValueReader<byte[]>(schema, memberTraits);

    public object VisitTimestamp(Schema<DateTimeOffset> schema) =>
        new ScalarXmlValueReader<DateTimeOffset>(schema, memberTraits);

    public object VisitDocument(Schema<Document> schema) => inner.CompileValue(schema);

    public object VisitNullable<T>(NullableSchema<T> schema)
        where T : struct =>
        new NullableXmlValueReader<T>(inner.CompileValue(schema.TargetSchema, memberTraits));

    public object VisitStreamingBlob(Schema<Stream> schema) => inner.CompileValue(schema);

    public object VisitEventStream<TEvent>(EventStreamSchema<TEvent> schema) =>
        inner.CompileValue(schema);

    public object VisitList<TCollection, TElement, TBuilder>(
        IListSchema<TCollection, TElement, TBuilder> schema
    ) => inner.CompileValue((Schema<TCollection>)schema);

    public object VisitMap<TDictionary, TValue, TBuilder>(
        IMapSchema<TDictionary, TValue, TBuilder> schema
    ) => inner.CompileValue((Schema<TDictionary>)schema);

    public object VisitStruct<T, TBuilder>(IStructSchema<T, TBuilder> schema) =>
        inner.CompileValue((Schema<T>)schema);

    public object VisitUnion<T>(IUnionSchema<T> schema) => inner.CompileValue((Schema<T>)schema);

    public object VisitStringEnum<T>(StringEnumSchema<T> schema)
        where T : IStringEnumValue<T> => new ScalarXmlValueReader<T>(schema, memberTraits);

    public object VisitIntEnum<T>(IntEnumSchema<T> schema)
        where T : struct, Enum => new ScalarXmlValueReader<T>(schema, memberTraits);
}

internal sealed class ScalarXmlValueReader<T>(
    Schema<T> schema,
    IReadOnlyDictionary<ShapeId, Trait> traits
) : IXmlValueReader<T>
{
    public T Read(XElement? element) => ReadScalar(schema, traits, element?.Value ?? string.Empty);
}

internal sealed class NullableXmlValueReader<T>(IXmlValueReader<T> inner) : IXmlValueReader<T?>
    where T : struct
{
    public T? Read(XElement? element) => element is null ? null : inner.Read(element);
}

internal sealed class XmlMemberReaderCompiler<TContainer, TBuilder>(XmlReaderCompiler compiler)
    : IMemberVisitor<TContainer, TBuilder>
{
    private readonly List<IXmlMemberReader<TBuilder>> readers = [];

    public IXmlMemberReader<TBuilder>[] Readers => [.. readers];

    public void Visit<TValue>(IMemberSchema<TContainer, TBuilder, TValue> member)
    {
        if (XmlTraits.IsXmlFlattened(member))
        {
            readers.Add(CreateFlattenedReader(member));
            return;
        }

        readers.Add(
            new XmlMemberReader<TContainer, TBuilder, TValue>(
                member,
                compiler.CompileValue(member.TargetSchema, member.MemberTraits)
            )
        );
    }

    private IXmlMemberReader<TBuilder> CreateFlattenedReader<TValue>(
        IMemberSchema<TContainer, TBuilder, TValue> member
    )
    {
        var target = member.TargetSchema.Resolved;
        return target switch
        {
            IListSchema list => CreateFlattenedListReader(member, (dynamic)list),
            IMapSchema map => CreateFlattenedMapReader(member, (dynamic)map),
            _ => new FlattenedXmlMemberReader<TContainer, TBuilder, TValue>(
                member,
                compiler.CompileValue(member.TargetSchema, member.MemberTraits)
            ),
        };
    }

    private FlattenedListXmlMemberReader<
        TContainer,
        TBuilder,
        TValue,
        TElement,
        TCollectionBuilder
    > CreateFlattenedListReader<TValue, TElement, TCollectionBuilder>(
        IMemberSchema<TContainer, TBuilder, TValue> member,
        IListSchema<TValue, TElement, TCollectionBuilder> list
    ) =>
        new(
            member,
            list,
            compiler.CompileValue(
                list.TypedElementMember.TargetSchema,
                list.TypedElementMember.MemberTraits
            )
        );

    private FlattenedMapXmlMemberReader<
        TContainer,
        TBuilder,
        TValue,
        TMapValue,
        TMapBuilder
    > CreateFlattenedMapReader<TValue, TMapValue, TMapBuilder>(
        IMemberSchema<TContainer, TBuilder, TValue> member,
        IMapSchema<TValue, TMapValue, TMapBuilder> map
    ) =>
        new(
            member,
            map,
            compiler.CompileValue(
                map.TypedValueMember.TargetSchema,
                map.TypedValueMember.MemberTraits
            )
        );
}

internal sealed class XmlMemberReader<TContainer, TBuilder, TValue>(
    IMemberSchema<TContainer, TBuilder, TValue> member,
    IXmlValueReader<TValue> valueReader
) : IXmlMemberReader<TBuilder>
{
    public string Name => ElementName(member);

    public bool IsRequired => member.IsRequired;

    public void ReadInto(TBuilder builder, XElement element)
    {
        if (XmlTraits.IsXmlAttribute(member))
        {
            var attr = element.Attribute(AttributeName(element, Name));
            if (attr is not null)
            {
                member.SetValue(
                    builder,
                    ReadScalar(member.TargetSchema, member.MemberTraits, attr.Value)
                );
            }
            else if (member.IsRequired)
            {
                throw new MissingRequiredMemberException(member.Name);
            }

            return;
        }

        if (XmlTraits.IsXmlFlattened(member))
        {
            throw new InvalidOperationException(
                $"Flattened XML member '{member.Name}' was not compiled as a flattened reader."
            );
        }

        var child = ChildElement(element, Name);
        if (child is null && string.Equals(element.Name.LocalName, Name, StringComparison.Ordinal))
        {
            child = element;
        }
        if (child is not null)
        {
            member.SetValue(builder, valueReader.Read(child));
        }
        else if (member.IsRequired)
        {
            throw new MissingRequiredMemberException(member.Name);
        }
    }
}

internal sealed class FlattenedXmlMemberReader<TContainer, TBuilder, TValue>(
    IMemberSchema<TContainer, TBuilder, TValue> member,
    IXmlValueReader<TValue> valueReader
) : IXmlMemberReader<TBuilder>
{
    public string Name => ElementName(member);

    public bool IsRequired => member.IsRequired;

    public void ReadInto(TBuilder builder, XElement element)
    {
        var child = ChildElement(element, Name);
        if (child is not null)
        {
            member.SetValue(builder, valueReader.Read(child));
        }
        else if (member.IsRequired)
        {
            throw new MissingRequiredMemberException(member.Name);
        }
    }
}

internal sealed class FlattenedListXmlMemberReader<
    TContainer,
    TBuilder,
    TCollection,
    TElement,
    TCollectionBuilder
>(
    IMemberSchema<TContainer, TBuilder, TCollection> member,
    IListSchema<TCollection, TElement, TCollectionBuilder> list,
    IXmlValueReader<TElement> elementReader
) : IXmlMemberReader<TBuilder>
{
    public string Name => ElementName(member);

    public bool IsRequired => member.IsRequired;

    public void ReadInto(TBuilder builder, XElement element)
    {
        var collectionBuilder = list.CreateTypedBuilder();
        foreach (var child in ChildElements(element, Name))
        {
            list.Add(collectionBuilder, elementReader.Read(child));
        }

        member.SetValue(builder, list.Build(collectionBuilder));
    }
}

internal sealed class FlattenedMapXmlMemberReader<
    TContainer,
    TBuilder,
    TDictionary,
    TValue,
    TMapBuilder
>(
    IMemberSchema<TContainer, TBuilder, TDictionary> member,
    IMapSchema<TDictionary, TValue, TMapBuilder> map,
    IXmlValueReader<TValue> valueReader
) : IXmlMemberReader<TBuilder>
{
    private readonly string keyName = MapKeyName(map);
    private readonly string valueName = MapValueName(map.TypedValueMember);

    public string Name => ElementName(member);

    public bool IsRequired => member.IsRequired;

    public void ReadInto(TBuilder builder, XElement element)
    {
        var mapBuilder = map.CreateTypedBuilder();
        foreach (var child in ChildElements(element, Name))
        {
            var key = ChildElement(child, keyName)?.Value;
            if (key is null)
            {
                continue;
            }

            map.Add(mapBuilder, key, valueReader.Read(ChildElement(child, valueName)));
        }

        member.SetValue(builder, map.Build(mapBuilder));
    }
}

internal sealed class StructureXmlValueReader<T, TBuilder>(
    Func<TBuilder> createBuilder,
    Func<TBuilder, T> build,
    IXmlMemberReader<TBuilder>[] memberReaders
) : IXmlValueReader<T>
{
    public T Read(XElement? element)
    {
        if (element is null)
        {
            return default!;
        }

        var builder = createBuilder();
        foreach (var memberReader in memberReaders)
        {
            memberReader.ReadInto(builder, element);
        }

        return build(builder);
    }
}

internal sealed class StructureXmlProjectionReader<TBuilder>(
    IXmlMemberReader<TBuilder>[] memberReaders
)
{
    public void ReadInto(TBuilder builder, XElement element)
    {
        foreach (var memberReader in memberReaders)
        {
            memberReader.ReadInto(builder, element);
        }
    }
}

internal sealed class ListXmlValueReader<TCollection, TElement, TBuilder>(
    IListSchema<TCollection, TElement, TBuilder> schema,
    IXmlValueReader<TElement> elementReader
) : IXmlValueReader<TCollection>
{
    public TCollection Read(XElement? element)
    {
        var builder = schema.CreateTypedBuilder();
        if (element is not null)
        {
            foreach (var child in ChildElements(element, ListItemName(schema)))
            {
                schema.Add(builder, elementReader.Read(child));
            }
        }

        return schema.Build(builder);
    }
}

internal sealed class MapXmlValueReader<TDictionary, TValue, TBuilder>(
    IMapSchema<TDictionary, TValue, TBuilder> schema,
    IXmlValueReader<TValue> valueReader
) : IXmlValueReader<TDictionary>
{
    private readonly string keyName = MapKeyName(schema);
    private readonly string valueName = MapValueName(schema.TypedValueMember);

    public TDictionary Read(XElement? element)
    {
        var builder = schema.CreateTypedBuilder();
        if (element is not null)
        {
            foreach (var entry in element.Elements())
            {
                var key = ChildElement(entry, keyName)?.Value;
                if (key is null)
                {
                    continue;
                }

                schema.Add(builder, key, valueReader.Read(ChildElement(entry, valueName)));
            }
        }

        return schema.Build(builder);
    }
}

internal sealed class XmlUnionCaseReaderCompiler<TUnion>(XmlReaderCompiler compiler)
    : IUnionCaseVisitor<TUnion>
{
    private readonly List<IXmlUnionCaseReader<TUnion>> readers = [];

    public IXmlUnionCaseReader<TUnion>[] Readers => [.. readers];

    public void Visit<TValue>(IUnionCaseSchema<TUnion, TValue> unionCase)
    {
        readers.Add(
            new XmlUnionCaseReader<TUnion, TValue>(
                unionCase,
                compiler.CompileValue(unionCase.TargetSchema, unionCase.Traits)
            )
        );
    }
}

internal sealed class XmlUnionCaseReader<TUnion, TValue>(
    IUnionCaseSchema<TUnion, TValue> unionCase,
    IXmlValueReader<TValue> valueReader
) : IXmlUnionCaseReader<TUnion>
{
    public string Name => unionCase.Name;

    public TUnion Read(XElement element) => unionCase.Create(valueReader.Read(element));
}

internal sealed class UnionXmlValueReader<T>(IXmlUnionCaseReader<T>[] caseReaders)
    : IXmlValueReader<T>
{
    private readonly Dictionary<string, IXmlUnionCaseReader<T>> readersByName =
        caseReaders.ToDictionary(reader => reader.Name, StringComparer.Ordinal);

    public T Read(XElement? element)
    {
        var child =
            element?.Elements().FirstOrDefault()
            ?? throw new InvalidOperationException("Union payload was empty.");
        return readersByName.TryGetValue(child.Name.LocalName, out var reader)
            ? reader.Read(child)
            : throw new InvalidOperationException(
                $"Unknown union member '{child.Name.LocalName}'."
            );
    }
}
