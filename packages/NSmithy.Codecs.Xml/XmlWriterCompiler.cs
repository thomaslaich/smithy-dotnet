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

internal interface IXmlMemberPlan<in TValue>
{
    void Write(XElement element, TValue value);
}

internal interface IXmlUnionCaseWriter<in TUnion>
{
    bool TryWrite(XElement element, TUnion value);
}

internal sealed class XmlWriterCompiler : ISchemaVisitor<object>
{
    private static readonly IReadOnlyDictionary<ShapeId, Trait> EmptyTraits =
        new Dictionary<ShapeId, Trait>();

    private readonly SchemaCompilationCache cache = new();

    public static IXmlValueWriter<T> Compile<T>(
        Schema<T> schema,
        bool materializeTopLevelDefaults = true,
        IReadOnlyDictionary<ShapeId, Trait>? memberTraits = null
    )
    {
        ArgumentNullException.ThrowIfNull(schema);
        return new XmlWriterCompiler().CompileTopLevelValue(
            schema,
            materializeTopLevelDefaults,
            memberTraits
        );
    }

    public static IXmlValueWriter<T> Compile<T, TBuilder>(
        StructProjection<T, TBuilder> projection,
        bool materializeTopLevelDefaults = true
    )
    {
        ArgumentNullException.ThrowIfNull(projection);
        var compiler = new XmlWriterCompiler();
        if (projection.Source.ValueSerializer is not { } valueSerializer)
        {
            var fallback = new XmlMemberWriterCompiler<T>(compiler, materializeTopLevelDefaults);
            projection.VisitMembers(fallback);
            return new FallbackStructureXmlValueWriter<T>(fallback.Writers);
        }

        var included = new XmlMemberCollector<T>();
        projection.VisitMembers(included);
        var visitor = new XmlMemberWriterCompiler<T>(
            compiler,
            materializeTopLevelDefaults,
            included.Members
        );
        projection.Source.VisitMembers(visitor);
        return new DirectStructureXmlValueWriter<T>(valueSerializer, visitor.Plans);
    }

    private IXmlValueWriter<T> CompileTopLevelValue<T>(
        Schema<T> schema,
        bool materializeTopLevelDefaults,
        IReadOnlyDictionary<ShapeId, Trait>? memberTraits
    )
    {
        if (schema.Resolved is IStructSchema<T> structure)
        {
            return CompileStructure(structure, materializeTopLevelDefaults);
        }

        return memberTraits is null ? CompileValue(schema) : CompileValue(schema, memberTraits);
    }

    public IXmlValueWriter<T> CompileValue<T>(Schema<T> schema) =>
        CompileValue(schema, EmptyTraits);

    public IXmlValueWriter<T> CompileValue<T>(
        Schema<T> schema,
        IReadOnlyDictionary<ShapeId, Trait> memberTraits
    )
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(memberTraits);

        var resolved = schema.Resolved;
        var writer =
            memberTraits.Count != 0
                ? (IXmlValueWriter<T>)
                    resolved.Accept(new MemberTraitXmlWriterCompiler(this, memberTraits))
                : cache.GetOrCompile<IXmlValueWriter<T>, DeferredXmlValueWriter<T>>(
                    resolved,
                    static () => new DeferredXmlValueWriter<T>(),
                    target => (IXmlValueWriter<T>)target.Accept(this)
                );
        var xmlNamespace = XmlTraits.GetXmlNamespace(resolved, memberTraits);
        return xmlNamespace is null
            ? writer
            : new NamespacedXmlValueWriter<T>(writer, xmlNamespace.Value);
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

    public object VisitStreamingBlob(Schema<Stream> schema) =>
        throw new NotSupportedException("XML codec does not support streaming blob schemas.");

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
        return CompileStructure(schema, materializeDefaults: true);
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

    private IXmlValueWriter<T> CompileStructure<T>(
        IStructSchema<T> schema,
        bool materializeDefaults
    )
    {
        var visitor = new XmlMemberWriterCompiler<T>(this, materializeDefaults);
        schema.VisitMembers(visitor);
        return schema.ValueSerializer is { } valueSerializer
            ? new DirectStructureXmlValueWriter<T>(valueSerializer, visitor.Plans)
            : new FallbackStructureXmlValueWriter<T>(visitor.Writers);
    }
}

internal sealed class NamespacedXmlValueWriter<T>(
    IXmlValueWriter<T> inner,
    XmlNamespace xmlNamespace
) : IXmlValueWriter<T>
{
    public void Write(XElement element, T value)
    {
        ApplyNamespace(element, xmlNamespace);
        inner.Write(element, value);
    }
}

internal sealed class DeferredXmlValueWriter<T>
    : IXmlValueWriter<T>,
        IDeferredCompilation<IXmlValueWriter<T>>
{
    public void Complete(IXmlValueWriter<T> compiled) => Set(compiled);

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
    bool materializeDefaults,
    ISet<IMemberSchema<TContainer>>? includedMembers = null
) : IMemberVisitor<TContainer>
{
    private readonly List<IXmlMemberWriter<TContainer>> writers = [];
    private readonly List<object?> plans = [];

    // An array, not the interface: the write path iterates this once per object, and
    // foreach over IReadOnlyList<T> goes through IEnumerable<T>.GetEnumerator, which
    // boxes List<T>.Enumerator — a heap allocation per structure written.
    public IXmlMemberWriter<TContainer>[] Writers => [.. writers];

    public object?[] Plans => [.. plans];

    public void Visit<TValue>(IMemberSchema<TContainer, TValue> member)
    {
        if (includedMembers is not null && !includedMembers.Contains(member))
        {
            plans.Add(null);
            return;
        }

        var plan = XmlTraits.IsXmlFlattened(member)
            ? CreateFlattenedPlan(member)
            : new XmlMemberPlan<TValue>(
                member,
                compiler.CompileValue(member.TargetSchema, member.MemberTraits),
                materializeDefaults
            );
        plans.Add(plan);
        writers.Add(new FallbackXmlMemberWriter<TContainer, TValue>(member, plan));
    }

    private IXmlMemberPlan<TValue> CreateFlattenedPlan<TValue>(
        IMemberSchema<TContainer, TValue> member
    )
    {
        var target = member.TargetSchema.Resolved;
        return target switch
        {
            IListSchema list => CreateFlattenedListPlan(member, (dynamic)list),
            IMapSchema map => CreateFlattenedMapPlan(member, (dynamic)map),
            _ => new FlattenedXmlMemberPlan<TValue>(
                member,
                compiler.CompileValue(member.TargetSchema, member.MemberTraits),
                materializeDefaults
            ),
        };
    }

    private FlattenedListXmlMemberPlan<TValue, TElement> CreateFlattenedListPlan<TValue, TElement>(
        IMemberSchema<TContainer, TValue> member,
        IListSchema<TValue, TElement> list
    ) =>
        new(
            member,
            list,
            compiler.CompileValue(
                list.TypedElementMember.TargetSchema,
                list.TypedElementMember.MemberTraits
            ),
            materializeDefaults
        );

    private FlattenedMapXmlMemberPlan<TValue, TMapValue> CreateFlattenedMapPlan<TValue, TMapValue>(
        IMemberSchema<TContainer, TValue> member,
        IMapSchema<TValue, TMapValue> map
    ) =>
        new(
            member,
            map,
            compiler.CompileValue(
                map.TypedValueMember.TargetSchema,
                map.TypedValueMember.MemberTraits
            ),
            materializeDefaults
        );
}

internal sealed class XmlMemberCollector<TContainer> : IMemberVisitor<TContainer>
{
    public ISet<IMemberSchema<TContainer>> Members { get; } =
        new HashSet<IMemberSchema<TContainer>>(ReferenceEqualityComparer.Instance);

    public void Visit<TValue>(IMemberSchema<TContainer, TValue> member) => Members.Add(member);
}

internal sealed class FallbackXmlMemberWriter<TContainer, TValue>(
    IMemberSchema<TContainer, TValue> member,
    IXmlMemberPlan<TValue> plan
) : IXmlMemberWriter<TContainer>
{
    public void Write(XElement element, TContainer container) =>
        plan.Write(element, member.GetValue(container));
}

internal sealed class XmlMemberPlan<TValue>(
    ITargetedMemberSchema<TValue> member,
    IXmlValueWriter<TValue> valueWriter,
    bool materializeDefault
) : IXmlMemberPlan<TValue>
{
    private readonly bool isRequired = member.IsRequired;

    // Constant per member, so resolved at compile time rather than per write.
    private readonly (bool Present, TValue? Value) memberDefault = ResolveDefault(
        member.TargetSchema,
        member.MemberTraits,
        materializeDefault
    );

    public void Write(XElement element, TValue value)
    {
        if (value is null && !isRequired)
        {
            if (!memberDefault.Present)
            {
                return;
            }

            value = memberDefault.Value!;
        }

        if (XmlTraits.IsXmlAttribute(member))
        {
            element.SetAttributeValue(
                AttributeName(element, ElementName(member)),
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

        var child = new XElement(ChildElementName(element, ElementName(member)));
        valueWriter.Write(child, value);
        element.Add(child);
    }
}

internal sealed class FlattenedXmlMemberPlan<TValue>(
    ITargetedMemberSchema<TValue> member,
    IXmlValueWriter<TValue> valueWriter,
    bool materializeDefault
) : IXmlMemberPlan<TValue>
{
    private readonly bool isRequired = member.IsRequired;

    // Constant per member, so resolved at compile time rather than per write.
    private readonly (bool Present, TValue? Value) memberDefault = ResolveDefault(
        member.TargetSchema,
        member.MemberTraits,
        materializeDefault
    );

    public void Write(XElement element, TValue value)
    {
        if (value is null && !isRequired)
        {
            if (!memberDefault.Present)
            {
                return;
            }

            value = memberDefault.Value!;
        }

        var child = new XElement(ChildElementName(element, ElementName(member)));
        valueWriter.Write(child, value);
        element.Add(child);
    }
}

internal sealed class FlattenedListXmlMemberPlan<TCollection, TElement>(
    ITargetedMemberSchema<TCollection> member,
    IListSchema<TCollection, TElement> list,
    IXmlValueWriter<TElement> elementWriter,
    bool materializeDefault
) : IXmlMemberPlan<TCollection>
{
    private readonly bool isRequired = member.IsRequired;

    // Constant per member, so resolved at compile time rather than per write.
    private readonly (bool Present, TCollection? Value) memberDefault = ResolveDefault(
        member.TargetSchema,
        member.MemberTraits,
        materializeDefault
    );

    public void Write(XElement element, TCollection value)
    {
        if (value is null && !isRequired)
        {
            if (!memberDefault.Present)
            {
                return;
            }

            value = memberDefault.Value!;
        }

        if (value is null)
        {
            return;
        }

        foreach (var item in list.GetElements(value))
        {
            var child = new XElement(ChildElementName(element, ElementName(member)));
            elementWriter.Write(child, item);
            element.Add(child);
        }
    }
}

internal sealed class FlattenedMapXmlMemberPlan<TDictionary, TValue>(
    ITargetedMemberSchema<TDictionary> member,
    IMapSchema<TDictionary, TValue> map,
    IXmlValueWriter<TValue> valueWriter,
    bool materializeDefault
) : IXmlMemberPlan<TDictionary>
{
    private readonly bool isRequired = member.IsRequired;
    private readonly string keyName = MapKeyName(map);
    private readonly string valueName = MapValueName(map.TypedValueMember);

    // Constant per member, so resolved at compile time rather than per write.
    private readonly (bool Present, TDictionary? Value) memberDefault = ResolveDefault(
        member.TargetSchema,
        member.MemberTraits,
        materializeDefault
    );

    public void Write(XElement element, TDictionary value)
    {
        if (value is null && !isRequired)
        {
            if (!memberDefault.Present)
            {
                return;
            }

            value = memberDefault.Value!;
        }

        if (value is null)
        {
            return;
        }

        foreach (var entry in map.GetEntries(value))
        {
            var child = new XElement(ChildElementName(element, ElementName(member)));
            var keyElement = new XElement(ChildElementName(child, keyName), entry.Key);
            ApplyNamespace(
                keyElement,
                XmlTraits.GetXmlNamespace(map.KeyMember.Target, map.KeyMember.MemberTraits)
            );
            child.Add(keyElement);
            var valueElement = new XElement(ChildElementName(child, valueName));
            valueWriter.Write(valueElement, entry.Value);
            child.Add(valueElement);
            element.Add(child);
        }
    }
}

internal readonly struct XmlStructMemberWriter(XElement element, object?[] memberPlans)
    : IStructMemberWriter
{
    public void WriteMember<TValue>(int index, TValue value)
    {
        if (memberPlans[index] is { } plan)
        {
            ((IXmlMemberPlan<TValue>)plan).Write(element, value);
        }
    }
}

internal sealed class DirectStructureXmlValueWriter<T>(
    IStructValueSerializer<T> valueSerializer,
    object?[] memberPlans
) : IXmlValueWriter<T>
{
    public void Write(XElement element, T value)
    {
        if (value is null)
        {
            return;
        }

        var memberWriter = new XmlStructMemberWriter(element, memberPlans);
        valueSerializer.WriteMembers(value, ref memberWriter);
    }
}

internal sealed class FallbackStructureXmlValueWriter<T>(IXmlMemberWriter<T>[] memberWriters)
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
            var child = new XElement(ChildElementName(element, itemName));
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
    private readonly string keyName = MapKeyName(schema);
    private readonly string valueName = MapValueName(schema.TypedValueMember);

    public void Write(XElement element, TDictionary value)
    {
        if (value is null)
        {
            return;
        }

        foreach (var entry in schema.GetEntries(value))
        {
            var entryElement = new XElement(ChildElementName(element, "entry"));
            var keyElement = new XElement(ChildElementName(entryElement, keyName), entry.Key);
            ApplyNamespace(
                keyElement,
                XmlTraits.GetXmlNamespace(schema.KeyMember.Target, schema.KeyMember.MemberTraits)
            );
            entryElement.Add(keyElement);
            var valueElement = new XElement(ChildElementName(entryElement, valueName));
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

    public IXmlUnionCaseWriter<TUnion>[] Writers => [.. writers];

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

        var child = new XElement(ChildElementName(element, unionCase.Name));
        valueWriter.Write(child, unionCase.GetValue(value));
        element.Add(child);
        return true;
    }
}

internal sealed class UnionXmlValueWriter<T>(IXmlUnionCaseWriter<T>[] caseWriters)
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
