using System.Numerics;
using System.Xml.Linq;
using NSmithy.Core;
using NSmithy.Core.Serde;
using static NSmithy.Codecs.Xml.XmlWire;

namespace NSmithy.Codecs.Xml;

internal interface IXmlValueWriter<in T>
{
    void Write(XElement element, T value);
}

internal interface IXmlMemberWriter<in TContainer>
{
    void Write(XElement element, TContainer container);
}

internal interface IXmlUnionCaseWriter<in TUnion>
{
    bool TryWrite(XElement element, TUnion value);
}

internal sealed class XmlWriterCompiler : ISchemaVisitor<object>
{
    private static readonly IReadOnlyDictionary<ShapeId, Trait> EmptyTraits =
        new Dictionary<ShapeId, Trait>();

    private readonly Dictionary<Schema, object> cache = new(ReferenceEqualityComparer.Instance);

    public static IXmlValueWriter<T> Compile<T>(
        Schema<T> schema,
        bool materializeTopLevelDefaults = true
    )
    {
        ArgumentNullException.ThrowIfNull(schema);
        return new XmlWriterCompiler().CompileTopLevelValue(schema, materializeTopLevelDefaults);
    }

    public static StructureXmlValueWriter<T> Compile<T, TBuilder>(
        StructProjection<T, TBuilder> projection,
        bool materializeTopLevelDefaults = true
    )
    {
        ArgumentNullException.ThrowIfNull(projection);
        var compiler = new XmlWriterCompiler();
        var visitor = new XmlMemberWriterCompiler<T>(compiler, materializeTopLevelDefaults);
        projection.VisitMembers(visitor);
        return new StructureXmlValueWriter<T>(visitor.Writers);
    }

    private IXmlValueWriter<T> CompileTopLevelValue<T>(
        Schema<T> schema,
        bool materializeTopLevelDefaults
    )
    {
        if (schema.Resolved is IStructSchema<T> structure)
        {
            return CompileStructure(structure, materializeTopLevelDefaults);
        }

        return CompileValue(schema);
    }

    public IXmlValueWriter<T> CompileValue<T>(Schema<T> schema) =>
        CompileValue(schema, EmptyTraits);

    public IXmlValueWriter<T> CompileValue<T>(
        Schema<T> schema,
        IReadOnlyDictionary<ShapeId, Trait> memberTraits
    )
    {
        var resolved = schema.Resolved;
        if (memberTraits.Count != 0)
        {
            return (IXmlValueWriter<T>)
                resolved.Accept(new MemberTraitXmlWriterCompiler(this, memberTraits));
        }

        if (cache.TryGetValue(resolved, out var cached))
        {
            return (IXmlValueWriter<T>)cached;
        }

        var deferred = new DeferredXmlValueWriter<T>();
        cache.Add(resolved, deferred);
        deferred.Set((IXmlValueWriter<T>)resolved.Accept(this));
        return deferred;
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
        where T : struct => new NullableXmlValueWriter<T>(CompileValue(schema.TargetSchema));

    public object VisitEventStream<TEvent>(EventStreamSchema<TEvent> schema) =>
        throw new NotSupportedException("XML codec does not support event stream schemas.");

    public object VisitList<TCollection, TElement, TBuilder>(
        IListSchema<TCollection, TElement, TBuilder> schema
    ) =>
        new ListXmlValueWriter<TCollection, TElement>(
            schema,
            CompileValue(
                schema.TypedElementMember.TargetSchema,
                schema.TypedElementMember.MemberTraits
            )
        );

    public object VisitMap<TDictionary, TValue, TBuilder>(
        IMapSchema<TDictionary, TValue, TBuilder> schema
    ) =>
        new MapXmlValueWriter<TDictionary, TValue>(
            schema,
            CompileValue(schema.TypedValueMember.TargetSchema, schema.TypedValueMember.MemberTraits)
        );

    public object VisitStruct<T, TBuilder>(IStructSchema<T, TBuilder> schema)
    {
        var visitor = new XmlMemberWriterCompiler<T>(this, materializeDefaults: true);
        schema.VisitMembers(visitor);
        return new StructureXmlValueWriter<T>(visitor.Writers);
    }

    public object VisitUnion<T>(IUnionSchema<T> schema)
    {
        var visitor = new XmlUnionCaseWriterCompiler<T>(this);
        schema.VisitCases(visitor);
        return new UnionXmlValueWriter<T>(visitor.Writers);
    }

    public object VisitStringEnum<T>(StringEnumSchema<T> schema)
        where T : IStringEnumValue<T> => Scalar(schema, EmptyTraits);

    public object VisitIntEnum<T>(IntEnumSchema<T> schema)
        where T : struct, Enum => Scalar(schema, EmptyTraits);

    private static ScalarXmlValueWriter<T> Scalar<T>(
        Schema<T> schema,
        IReadOnlyDictionary<ShapeId, Trait> traits
    ) => new(schema, traits);

    private StructureXmlValueWriter<T> CompileStructure<T>(
        IStructSchema<T> schema,
        bool materializeDefaults
    )
    {
        var visitor = new XmlMemberWriterCompiler<T>(this, materializeDefaults);
        schema.VisitMembers(visitor);
        return new StructureXmlValueWriter<T>(visitor.Writers);
    }
}

internal sealed class DeferredXmlValueWriter<T> : IXmlValueWriter<T>
{
    private IXmlValueWriter<T>? inner;

    public void Set(IXmlValueWriter<T> writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        inner = writer;
    }

    public void Write(XElement element, T value)
    {
        if (inner is null)
        {
            throw new InvalidOperationException("XML writer has not been initialized.");
        }

        inner.Write(element, value);
    }
}

internal sealed class MemberTraitXmlWriterCompiler(
    XmlWriterCompiler inner,
    IReadOnlyDictionary<ShapeId, Trait> memberTraits
) : ISchemaVisitor<object>
{
    public object VisitBoolean(Schema<bool> schema) =>
        new ScalarXmlValueWriter<bool>(schema, memberTraits);

    public object VisitByte(Schema<sbyte> schema) =>
        new ScalarXmlValueWriter<sbyte>(schema, memberTraits);

    public object VisitShort(Schema<short> schema) =>
        new ScalarXmlValueWriter<short>(schema, memberTraits);

    public object VisitInteger(Schema<int> schema) =>
        new ScalarXmlValueWriter<int>(schema, memberTraits);

    public object VisitLong(Schema<long> schema) =>
        new ScalarXmlValueWriter<long>(schema, memberTraits);

    public object VisitFloat(Schema<float> schema) =>
        new ScalarXmlValueWriter<float>(schema, memberTraits);

    public object VisitDouble(Schema<double> schema) =>
        new ScalarXmlValueWriter<double>(schema, memberTraits);

    public object VisitBigInteger(Schema<BigInteger> schema) =>
        new ScalarXmlValueWriter<BigInteger>(schema, memberTraits);

    public object VisitBigDecimal(Schema<decimal> schema) =>
        new ScalarXmlValueWriter<decimal>(schema, memberTraits);

    public object VisitString(Schema<string> schema) =>
        new ScalarXmlValueWriter<string>(schema, memberTraits);

    public object VisitBlob(Schema<byte[]> schema) =>
        new ScalarXmlValueWriter<byte[]>(schema, memberTraits);

    public object VisitTimestamp(Schema<DateTimeOffset> schema) =>
        new ScalarXmlValueWriter<DateTimeOffset>(schema, memberTraits);

    public object VisitDocument(Schema<Document> schema) => inner.CompileValue(schema);

    public object VisitNullable<T>(NullableSchema<T> schema)
        where T : struct =>
        new NullableXmlValueWriter<T>(inner.CompileValue(schema.TargetSchema, memberTraits));

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
        where T : IStringEnumValue<T> => new ScalarXmlValueWriter<T>(schema, memberTraits);

    public object VisitIntEnum<T>(IntEnumSchema<T> schema)
        where T : struct, Enum => new ScalarXmlValueWriter<T>(schema, memberTraits);
}

internal sealed class ScalarXmlValueWriter<T>(
    Schema<T> schema,
    IReadOnlyDictionary<ShapeId, Trait> traits
) : IXmlValueWriter<T>
{
    public void Write(XElement element, T value)
    {
        if (value is not null)
        {
            element.Value = FormatScalar(schema, traits, value);
        }
    }
}

internal sealed class NullableXmlValueWriter<T>(IXmlValueWriter<T> inner) : IXmlValueWriter<T?>
    where T : struct
{
    public void Write(XElement element, T? value)
    {
        if (value.HasValue)
        {
            inner.Write(element, value.Value);
        }
    }
}

internal sealed class XmlMemberWriterCompiler<TContainer>(
    XmlWriterCompiler compiler,
    bool materializeDefaults
) : IMemberVisitor<TContainer>
{
    private readonly List<IXmlMemberWriter<TContainer>> writers = [];

    public IReadOnlyList<IXmlMemberWriter<TContainer>> Writers => writers;

    public void Visit<TValue>(IMemberSchema<TContainer, TValue> member)
    {
        if (XmlTraits.IsXmlFlattened(member))
        {
            writers.Add(CreateFlattenedWriter(member));
            return;
        }

        writers.Add(
            new XmlMemberWriter<TContainer, TValue>(
                member,
                compiler.CompileValue(member.TargetSchema, member.MemberTraits),
                materializeDefaults
            )
        );
    }

    private IXmlMemberWriter<TContainer> CreateFlattenedWriter<TValue>(
        IMemberSchema<TContainer, TValue> member
    )
    {
        var target = member.TargetSchema.Resolved;
        return target switch
        {
            IListSchema list => CreateFlattenedListWriter(member, (dynamic)list),
            IMapSchema map => CreateFlattenedMapWriter(member, (dynamic)map),
            _ => new FlattenedXmlMemberWriter<TContainer, TValue>(
                member,
                compiler.CompileValue(member.TargetSchema, member.MemberTraits),
                materializeDefaults
            ),
        };
    }

    private FlattenedListXmlMemberWriter<TContainer, TValue, TElement> CreateFlattenedListWriter<
        TValue,
        TElement
    >(IMemberSchema<TContainer, TValue> member, IListSchema<TValue, TElement> list) =>
        new(
            member,
            list,
            compiler.CompileValue(
                list.TypedElementMember.TargetSchema,
                list.TypedElementMember.MemberTraits
            ),
            materializeDefaults
        );

    private FlattenedMapXmlMemberWriter<TContainer, TValue, TMapValue> CreateFlattenedMapWriter<
        TValue,
        TMapValue
    >(IMemberSchema<TContainer, TValue> member, IMapSchema<TValue, TMapValue> map) =>
        new(
            member,
            map,
            compiler.CompileValue(map.TypedValueMember.TargetSchema, map.TypedValueMember.MemberTraits),
            materializeDefaults
        );
}

internal sealed class XmlMemberWriter<TContainer, TValue>(
    IMemberSchema<TContainer, TValue> member,
    IXmlValueWriter<TValue> valueWriter,
    bool materializeDefault
) : IXmlMemberWriter<TContainer>
{
    public void Write(XElement element, TContainer container)
    {
        var value = member.GetValue(container);
        if (value is null && !member.IsRequired)
        {
            if (
                !materializeDefault
                || !TryCreateDefaultValue(
                    member.TargetSchema,
                    member.MemberTraits,
                    out TValue? defaultValue
                )
            )
            {
                return;
            }

            value = defaultValue!;
        }

        if (XmlTraits.IsXmlAttribute(member))
        {
            element.SetAttributeValue(
                ElementName(member),
                FormatScalar(member.TargetSchema, member.MemberTraits, value)
            );
            return;
        }

        if (XmlTraits.IsXmlFlattened(member))
        {
            throw new InvalidOperationException(
                $"Flattened XML member '{member.Name}' was not compiled as a flattened writer."
            );
        }

        var child = new XElement(ElementName(member));
        valueWriter.Write(child, value);
        element.Add(child);
    }
}

internal sealed class FlattenedXmlMemberWriter<TContainer, TValue>(
    IMemberSchema<TContainer, TValue> member,
    IXmlValueWriter<TValue> valueWriter,
    bool materializeDefault
) : IXmlMemberWriter<TContainer>
{
    public void Write(XElement element, TContainer container)
    {
        var value = member.GetValue(container);
        if (value is null && !member.IsRequired)
        {
            if (
                !materializeDefault
                || !TryCreateDefaultValue(
                    member.TargetSchema,
                    member.MemberTraits,
                    out TValue? defaultValue
                )
            )
            {
                return;
            }

            value = defaultValue!;
        }

        var child = new XElement(ElementName(member));
        valueWriter.Write(child, value);
        element.Add(child);
    }
}

internal sealed class FlattenedListXmlMemberWriter<TContainer, TCollection, TElement>(
    IMemberSchema<TContainer, TCollection> member,
    IListSchema<TCollection, TElement> list,
    IXmlValueWriter<TElement> elementWriter,
    bool materializeDefault
) : IXmlMemberWriter<TContainer>
{
    public void Write(XElement element, TContainer container)
    {
        var value = member.GetValue(container);
        if (value is null && !member.IsRequired)
        {
            if (
                !materializeDefault
                || !TryCreateDefaultValue(
                    member.TargetSchema,
                    member.MemberTraits,
                    out TCollection? defaultValue
                )
            )
            {
                return;
            }

            value = defaultValue!;
        }

        if (value is null)
        {
            return;
        }

        foreach (var item in list.GetElements(value))
        {
            var child = new XElement(ElementName(member));
            elementWriter.Write(child, item);
            element.Add(child);
        }
    }
}

internal sealed class FlattenedMapXmlMemberWriter<TContainer, TDictionary, TValue>(
    IMemberSchema<TContainer, TDictionary> member,
    IMapSchema<TDictionary, TValue> map,
    IXmlValueWriter<TValue> valueWriter,
    bool materializeDefault
) : IXmlMemberWriter<TContainer>
{
    public void Write(XElement element, TContainer container)
    {
        var value = member.GetValue(container);
        if (value is null && !member.IsRequired)
        {
            if (
                !materializeDefault
                || !TryCreateDefaultValue(
                    member.TargetSchema,
                    member.MemberTraits,
                    out TDictionary? defaultValue
                )
            )
            {
                return;
            }

            value = defaultValue!;
        }

        if (value is null)
        {
            return;
        }

        foreach (var entry in map.GetEntries(value))
        {
            var child = new XElement(ElementName(member));
            child.Add(new XElement("key", entry.Key));
            var valueElement = new XElement("value");
            valueWriter.Write(valueElement, entry.Value);
            child.Add(valueElement);
            element.Add(child);
        }
    }
}

internal sealed class StructureXmlValueWriter<T>(IReadOnlyList<IXmlMemberWriter<T>> memberWriters)
    : IXmlValueWriter<T>
{
    public void Write(XElement element, T value)
    {
        if (value is null)
        {
            return;
        }

        foreach (var memberWriter in memberWriters)
        {
            memberWriter.Write(element, value);
        }
    }
}

internal sealed class ListXmlValueWriter<TCollection, TElement>(
    IListSchema<TCollection, TElement> schema,
    IXmlValueWriter<TElement> elementWriter
) : IXmlValueWriter<TCollection>
{
    public void Write(XElement element, TCollection value)
    {
        if (value is null)
        {
            return;
        }

        var itemName = ListItemName(schema);
        foreach (var item in schema.GetElements(value))
        {
            var child = new XElement(itemName);
            elementWriter.Write(child, item);
            element.Add(child);
        }
    }
}

internal sealed class MapXmlValueWriter<TDictionary, TValue>(
    IMapSchema<TDictionary, TValue> schema,
    IXmlValueWriter<TValue> valueWriter
) : IXmlValueWriter<TDictionary>
{
    public void Write(XElement element, TDictionary value)
    {
        if (value is null)
        {
            return;
        }

        foreach (var entry in schema.GetEntries(value))
        {
            var entryElement = new XElement("entry");
            entryElement.Add(new XElement("key", entry.Key));
            var valueElement = new XElement("value");
            valueWriter.Write(valueElement, entry.Value);
            entryElement.Add(valueElement);
            element.Add(entryElement);
        }
    }
}

internal sealed class XmlUnionCaseWriterCompiler<TUnion>(XmlWriterCompiler compiler)
    : IUnionCaseVisitor<TUnion>
{
    private readonly List<IXmlUnionCaseWriter<TUnion>> writers = [];

    public IReadOnlyList<IXmlUnionCaseWriter<TUnion>> Writers => writers;

    public void Visit<TValue>(IUnionCaseSchema<TUnion, TValue> unionCase)
    {
        writers.Add(
            new XmlUnionCaseWriter<TUnion, TValue>(
                unionCase,
                compiler.CompileValue(unionCase.TargetSchema, unionCase.Traits)
            )
        );
    }
}

internal sealed class XmlUnionCaseWriter<TUnion, TValue>(
    IUnionCaseSchema<TUnion, TValue> unionCase,
    IXmlValueWriter<TValue> valueWriter
) : IXmlUnionCaseWriter<TUnion>
{
    public bool TryWrite(XElement element, TUnion value)
    {
        if (!unionCase.Matches(value))
        {
            return false;
        }

        var child = new XElement(unionCase.Name);
        valueWriter.Write(child, unionCase.GetValue(value));
        element.Add(child);
        return true;
    }
}

internal sealed class UnionXmlValueWriter<T>(IReadOnlyList<IXmlUnionCaseWriter<T>> caseWriters)
    : IXmlValueWriter<T>
{
    public void Write(XElement element, T value)
    {
        foreach (var caseWriter in caseWriters)
        {
            if (caseWriter.TryWrite(element, value))
            {
                return;
            }
        }

        throw new InvalidOperationException($"No union case matched '{typeof(T).Name}'.");
    }
}
