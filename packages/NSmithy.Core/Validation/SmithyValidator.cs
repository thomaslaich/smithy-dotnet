using System.Collections;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
using NSmithy.Core.Serde;

namespace NSmithy.Core.Validation;

public interface ISmithyValidator<in T>
{
    /// <summary>
    /// Throws <see cref="ValidationException"/> — the modeled
    /// <c>smithy.framework#ValidationException</c> — when the value violates a constraint.
    /// </summary>
    void Validate(T value);

    IReadOnlyList<SmithyValidationError> GetErrors(T value);
}

/// <summary>
/// One constraint violation. <see cref="Path"/> is a JSONPointer (RFC 6901) into the validated
/// value, matching what <c>smithy.framework#ValidationExceptionField</c> documents its path member
/// to be; the empty string points at the value itself.
/// </summary>
public sealed record SmithyValidationError(
    string Path,
    ShapeId ShapeId,
    ShapeId ConstraintId,
    string Message
);

public static class SmithyValidator
{
    /// <summary>
    /// Compiles a validator for the schema, or returns null when nothing reachable from it
    /// carries a validation constraint, so callers can skip validation entirely.
    /// </summary>
    public static ISmithyValidator<T>? FromSchema<T>(Schema<T> schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        var validator = ValidatorCompiler.Compile(schema);
        return ReferenceEquals(validator, NoOpValidator<T>.Instance)
            ? null
            : new SchemaValidator<T>(validator);
    }
}

/// <summary>
/// JSONPointer (RFC 6901) path building. The root value is the empty string; every step appends
/// <c>/</c> plus the escaped token.
/// </summary>
internal static class JsonPointer
{
    public const string Root = "";

    public static string Append(string path, string token) =>
        path
        + "/"
        + token
            .Replace("~", "~0", StringComparison.Ordinal)
            .Replace("/", "~1", StringComparison.Ordinal);

    public static string Append(string path, int index) =>
        string.Create(CultureInfo.InvariantCulture, $"{path}/{index}");

    /// <summary>Renders a path for an error message, since the root pointer is empty.</summary>
    public static string Describe(string path) => path.Length == 0 ? "the value" : $"'{path}'";
}

internal sealed class SchemaValidator<T> : ISmithyValidator<T>
{
    private readonly IValueValidator<T> validator;

    public SchemaValidator(IValueValidator<T> validator)
    {
        this.validator = validator;

        // Force the top-level body now so a schema the validator cannot compile fails when the
        // protocol is built rather than on the first request. Nested bodies stay deferred, which
        // is what keeps recursive schemas from recursing forever at compile time.
        if (validator is IEagerlyCompilable eager)
        {
            eager.Compile();
        }
    }

    public void Validate(T value)
    {
        var errors = GetErrors(value);
        if (errors.Count > 0)
        {
            throw ValidationException.FromErrors(errors);
        }
    }

    public IReadOnlyList<SmithyValidationError> GetErrors(T value)
    {
        List<SmithyValidationError> errors = [];
        validator.Validate(value, JsonPointer.Root, errors);
        return new ReadOnlyCollection<SmithyValidationError>(errors);
    }
}

internal interface IEagerlyCompilable
{
    void Compile();
}

internal interface IValueValidator<in T>
{
    void Validate(T value, string path, List<SmithyValidationError> errors);
}

internal static class ConstraintTraits
{
    public static readonly ShapeId Required = new("smithy.api", "required");
    public static readonly ShapeId Length = new("smithy.api", "length");
    public static readonly ShapeId Range = new("smithy.api", "range");
    public static readonly ShapeId Pattern = new("smithy.api", "pattern");
    public static readonly ShapeId UniqueItems = new("smithy.api", "uniqueItems");
    public static readonly ShapeId Enum = new("smithy.api", "enum");

    public static bool AnyIn(IReadOnlyDictionary<ShapeId, Trait> traits) =>
        traits.Count > 0
        && (
            traits.ContainsKey(Length)
            || traits.ContainsKey(Range)
            || traits.ContainsKey(Pattern)
            || traits.ContainsKey(UniqueItems)
        );
}

internal static class ValidatorCompiler
{
    /// <summary>
    /// Returns a validator for the schema, or the no-op singleton when nothing reachable from it
    /// can fail validation. Compilation of the schema body is deferred until first use so that
    /// recursive schemas (built with <see cref="Schemas.Lazy{T}"/>) never recurse at compile time.
    /// </summary>
    public static IValueValidator<T> Compile<T>(Schema<T> schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        return RequiresValidation(schema, [])
            ? new DeferredValidator<T>(schema)
            : NoOpValidator<T>.Instance;
    }

    internal static IValueValidator<T> CompileBody<T>(Schema<T> schema)
    {
        var constraints = ConstraintValidator<T>.FromTraits(schema.Traits, schema.Id, schema);
        var structural = (IValueValidator<T>)schema.Resolved.Accept(StructuralCompiler.Instance);

        return ReferenceEquals(structural, NoOpValidator<T>.Instance) ? constraints
            : ReferenceEquals(constraints, NoOpValidator<T>.Instance) ? structural
            : new CompositeValidator<T>(constraints, structural);
    }

    private static bool RequiresValidation(Schema schema, HashSet<Schema> visited)
    {
        // Trait check before the visited check: distinct member/overlay schemas can share one
        // resolved target, and each edge may carry its own constraint traits.
        if (ConstraintTraits.AnyIn(schema.Traits))
        {
            return true;
        }

        var resolved = schema.Resolved;
        if (!visited.Add(resolved))
        {
            return false;
        }

        return resolved.Accept(new ValidationRequirementVisitor(visited));
    }

    private static bool RequiresMemberValidation(IMemberSchema member, HashSet<Schema> visited) =>
        member.IsRequired
        || ConstraintTraits.AnyIn(member.MemberTraits)
        || RequiresValidation(member.Target, visited);

    private static bool RequiresMapValidation<TDictionary, TValue>(
        IMapSchema<TDictionary, TValue> map,
        HashSet<Schema> visited
    ) =>
        RequiresMemberValidation(map.TypedKeyMember, visited)
        || RequiresMemberValidation(map.TypedValueMember, visited);

    private sealed class StructuralCompiler : ISchemaVisitor<object>
    {
        public static StructuralCompiler Instance { get; } = new();

        public object VisitBoolean(Schema<bool> schema) => NoOpValidator<bool>.Instance;

        public object VisitByte(Schema<sbyte> schema) => NoOpValidator<sbyte>.Instance;

        public object VisitShort(Schema<short> schema) => NoOpValidator<short>.Instance;

        public object VisitInteger(Schema<int> schema) => NoOpValidator<int>.Instance;

        public object VisitLong(Schema<long> schema) => NoOpValidator<long>.Instance;

        public object VisitFloat(Schema<float> schema) => NoOpValidator<float>.Instance;

        public object VisitDouble(Schema<double> schema) => NoOpValidator<double>.Instance;

        public object VisitBigInteger(Schema<BigInteger> schema) =>
            NoOpValidator<BigInteger>.Instance;

        public object VisitBigDecimal(Schema<decimal> schema) => NoOpValidator<decimal>.Instance;

        public object VisitString(Schema<string> schema) => NoOpValidator<string>.Instance;

        public object VisitBlob(Schema<byte[]> schema) => NoOpValidator<byte[]>.Instance;

        public object VisitTimestamp(Schema<DateTimeOffset> schema) =>
            NoOpValidator<DateTimeOffset>.Instance;

        public object VisitDocument(Schema<Document> schema) => NoOpValidator<Document>.Instance;

        public object VisitNullable<T>(NullableSchema<T> schema)
            where T : struct => new NullableValidator<T>(Compile(schema.TargetSchema));

        public object VisitStreamingBlob(Schema<Stream> schema) => NoOpValidator<Stream>.Instance;

        public object VisitEventStream<TEvent>(EventStreamSchema<TEvent> schema) =>
            NoOpValidator<IAsyncEnumerable<TEvent>>.Instance;

        public object VisitList<TCollection, TElement, TBuilder>(
            IListSchema<TCollection, TElement, TBuilder> schema
        ) => new ListValidator<TCollection, TElement>(schema);

        public object VisitMap<TDictionary, TValue, TBuilder>(
            IMapSchema<TDictionary, TValue, TBuilder> schema
        ) => new MapValidator<TDictionary, TValue>(schema);

        public object VisitStruct<T, TBuilder>(IStructSchema<T, TBuilder> schema) =>
            new StructValidator<T>(schema);

        public object VisitUnion<T>(IUnionSchema<T> schema) => new UnionValidator<T>(schema);

        public object VisitStringEnum<T>(StringEnumSchema<T> schema)
            where T : IStringEnumValue<T> =>
            schema.Values.Count == 0
                ? NoOpValidator<T>.Instance
                : new StringEnumValidator<T>(schema);

        public object VisitIntEnum<T>(IntEnumSchema<T> schema)
            where T : struct, Enum =>
            schema.Values.Count == 0 ? NoOpValidator<T>.Instance : new IntEnumValidator<T>(schema);
    }

    private sealed class ValidationRequirementVisitor(HashSet<Schema> visited)
        : ISchemaVisitor<bool>
    {
        public bool VisitBoolean(Schema<bool> schema) => false;

        public bool VisitByte(Schema<sbyte> schema) => false;

        public bool VisitShort(Schema<short> schema) => false;

        public bool VisitInteger(Schema<int> schema) => false;

        public bool VisitLong(Schema<long> schema) => false;

        public bool VisitFloat(Schema<float> schema) => false;

        public bool VisitDouble(Schema<double> schema) => false;

        public bool VisitBigInteger(Schema<BigInteger> schema) => false;

        public bool VisitBigDecimal(Schema<decimal> schema) => false;

        public bool VisitString(Schema<string> schema) => false;

        public bool VisitBlob(Schema<byte[]> schema) => false;

        public bool VisitTimestamp(Schema<DateTimeOffset> schema) => false;

        public bool VisitDocument(Schema<Document> schema) => false;

        public bool VisitNullable<T>(NullableSchema<T> schema)
            where T : struct => RequiresValidation(schema.TargetSchema, visited);

        public bool VisitStreamingBlob(Schema<Stream> schema) => false;

        public bool VisitEventStream<TEvent>(EventStreamSchema<TEvent> schema) => false;

        public bool VisitList<TCollection, TElement, TBuilder>(
            IListSchema<TCollection, TElement, TBuilder> schema
        ) => RequiresMemberValidation(schema.TypedElementMember, visited);

        public bool VisitMap<TDictionary, TValue, TBuilder>(
            IMapSchema<TDictionary, TValue, TBuilder> schema
        ) => RequiresMapValidation(schema, visited);

        public bool VisitStruct<T, TBuilder>(IStructSchema<T, TBuilder> schema)
        {
            var visitor = new ValidationRequirementMemberVisitor<T>(visited);
            schema.VisitMembers(visitor);
            return visitor.RequiresValidation;
        }

        public bool VisitUnion<T>(IUnionSchema<T> schema) =>
            schema.Cases.Any(unionCase =>
                ConstraintTraits.AnyIn(unionCase.Traits)
                || RequiresValidation(unionCase.Target, visited)
            );

        public bool VisitStringEnum<T>(StringEnumSchema<T> schema)
            where T : IStringEnumValue<T> => schema.Values.Count > 0;

        public bool VisitIntEnum<T>(IntEnumSchema<T> schema)
            where T : struct, Enum => schema.Values.Count > 0;
    }

    private sealed class ValidationRequirementMemberVisitor<T>(HashSet<Schema> visited)
        : IMemberVisitor<T>
    {
        public bool RequiresValidation { get; private set; }

        public void Visit<TValue>(IMemberSchema<T, TValue> member)
        {
            if (RequiresValidation)
            {
                return;
            }

            RequiresValidation = RequiresMemberValidation(member, visited);
        }
    }

    /// <summary>
    /// One compilation path for every kind of member — structure members, list elements, map keys
    /// and map values are the same thing here: an edge with its own traits onto a target schema.
    /// </summary>
    internal static MemberValueValidator<TValue> CompileMember<TValue>(
        ITargetedMemberSchema<TValue> member
    ) =>
        new(
            // The member's own traits are compiled against the target's kind: a member schema is
            // itself ShapeKind.Member, and an optional member wraps its target in a nullable
            // schema, so neither the member nor the CLR type alone identifies the constrained shape.
            ConstraintValidator<TValue>.FromTraits(
                member.MemberTraits,
                member.Id,
                member.TargetSchema
            ),
            Compile(member.TargetSchema)
        );
}

internal readonly struct MemberValueValidator<T>(
    IValueValidator<T> memberConstraints,
    IValueValidator<T> targetValidator
)
{
    public bool IsNoOp =>
        ReferenceEquals(memberConstraints, NoOpValidator<T>.Instance)
        && ReferenceEquals(targetValidator, NoOpValidator<T>.Instance);

    public void Validate(T value, string path, List<SmithyValidationError> errors)
    {
        memberConstraints.Validate(value, path, errors);
        targetValidator.Validate(value, path, errors);
    }
}

internal sealed class DeferredValidator<T>(Schema<T> schema)
    : IValueValidator<T>,
        IEagerlyCompilable
{
    private readonly Lazy<IValueValidator<T>> body = new(() =>
        ValidatorCompiler.CompileBody(schema)
    );

    public void Compile() => _ = body.Value;

    public void Validate(T value, string path, List<SmithyValidationError> errors) =>
        body.Value.Validate(value, path, errors);
}

internal sealed class CompositeValidator<T>(IValueValidator<T> first, IValueValidator<T> second)
    : IValueValidator<T>
{
    public void Validate(T value, string path, List<SmithyValidationError> errors)
    {
        first.Validate(value, path, errors);
        second.Validate(value, path, errors);
    }
}

internal sealed class NullableValidator<T>(IValueValidator<T> targetValidator) : IValueValidator<T?>
    where T : struct
{
    public void Validate(T? value, string path, List<SmithyValidationError> errors)
    {
        if (value.HasValue)
        {
            targetValidator.Validate(value.Value, path, errors);
        }
    }
}

internal sealed class StructValidator<T> : IValueValidator<T>, IMemberVisitor<T>
{
    private readonly List<IMemberValidator<T>> members = [];

    public StructValidator(IStructSchema<T> schema)
    {
        schema.VisitMembers(this);
    }

    public void Visit<TValue>(IMemberSchema<T, TValue> member)
    {
        var valueValidator = ValidatorCompiler.CompileMember(member);
        if (member.IsRequired || !valueValidator.IsNoOp)
        {
            members.Add(new MemberValidator<T, TValue>(member, valueValidator));
        }
    }

    public void Validate(T value, string path, List<SmithyValidationError> errors)
    {
        if (value is null)
        {
            return;
        }

        foreach (var member in members)
        {
            member.Validate(value, path, errors);
        }
    }
}

internal interface IMemberValidator<in TContainer>
{
    void Validate(TContainer container, string path, List<SmithyValidationError> errors);
}

internal sealed class MemberValidator<TContainer, TValue>(
    IMemberSchema<TContainer, TValue> member,
    MemberValueValidator<TValue> valueValidator
) : IMemberValidator<TContainer>
{
    public void Validate(TContainer container, string path, List<SmithyValidationError> errors)
    {
        var memberPath = JsonPointer.Append(path, member.Name);
        var value = member.GetValue(container);
        if (value is null)
        {
            if (member.IsRequired)
            {
                errors.Add(
                    new SmithyValidationError(
                        memberPath,
                        member.Target.Id,
                        ConstraintTraits.Required,
                        ConstraintMessages.Failed(memberPath, "Member must not be null")
                    )
                );
            }

            return;
        }

        valueValidator.Validate(value, memberPath, errors);
    }
}

internal sealed class ListValidator<TCollection, TElement> : IValueValidator<TCollection>
{
    private readonly IListSchema<TCollection, TElement> schema;
    private readonly ShapeId shapeId;
    private readonly bool uniqueItems;
    private readonly IEqualityComparer<TElement>? elementComparer;
    private readonly MemberValueValidator<TElement> elementValidator;

    public ListValidator(IListSchema<TCollection, TElement> schema)
    {
        this.schema = schema;
        var shape = (Schema<TCollection>)schema;
        shapeId = shape.Id;
        uniqueItems = shape.HasTrait(ConstraintTraits.UniqueItems);
        elementComparer = uniqueItems ? SchemaEqualityComparer.For(schema.ElementSchema) : null;
        elementValidator = ValidatorCompiler.CompileMember(schema.TypedElementMember);
    }

    public void Validate(TCollection value, string path, List<SmithyValidationError> errors)
    {
        if (value is null)
        {
            return;
        }

        HashSet<TElement>? seen = elementComparer is null ? null : new(elementComparer);
        var index = 0;
        foreach (var element in schema.GetElements(value))
        {
            if (seen is not null && !seen.Add(element))
            {
                errors.Add(
                    new SmithyValidationError(
                        path,
                        shapeId,
                        ConstraintTraits.UniqueItems,
                        ConstraintMessages.Failed(path, "Member must have unique values")
                    )
                );
                seen = null;
            }

            if (element is not null)
            {
                elementValidator.Validate(element, JsonPointer.Append(path, index), errors);
            }

            index++;
        }
    }
}

internal sealed class MapValidator<TDictionary, TValue>(IMapSchema<TDictionary, TValue> schema)
    : IValueValidator<TDictionary>
{
    private readonly MemberValueValidator<string> keyValidator = ValidatorCompiler.CompileMember(
        schema.TypedKeyMember
    );
    private readonly MemberValueValidator<TValue> valueValidator = ValidatorCompiler.CompileMember(
        schema.TypedValueMember
    );

    public void Validate(TDictionary value, string path, List<SmithyValidationError> errors)
    {
        if (value is null)
        {
            return;
        }

        foreach (var entry in schema.GetEntries(value))
        {
            // A key violation is reported at the map itself: the key is not a value sitting at
            // the entry's pointer, the entry's value is.
            keyValidator.Validate(entry.Key, path, errors);
            if (entry.Value is not null)
            {
                valueValidator.Validate(entry.Value, JsonPointer.Append(path, entry.Key), errors);
            }
        }
    }
}

internal sealed class UnionValidator<T> : IValueValidator<T>, IUnionCaseVisitor<T>
{
    private readonly List<IUnionCaseValidator<T>> cases = [];

    public UnionValidator(IUnionSchema<T> schema)
    {
        schema.VisitCases(this);
    }

    public void Visit<TValue>(IUnionCaseSchema<T, TValue> unionCase)
    {
        cases.Add(new UnionCaseValidator<T, TValue>(unionCase));
    }

    public void Validate(T value, string path, List<SmithyValidationError> errors)
    {
        if (value is null)
        {
            return;
        }

        foreach (var @case in cases)
        {
            if (@case.ValidateIfMatches(value, path, errors))
            {
                return;
            }
        }

        throw new InvalidOperationException($"No union case matched '{typeof(T).Name}'.");
    }
}

internal interface IUnionCaseValidator<in T>
{
    bool ValidateIfMatches(T value, string path, List<SmithyValidationError> errors);
}

internal sealed class UnionCaseValidator<TUnion, TValue>(IUnionCaseSchema<TUnion, TValue> unionCase)
    : IUnionCaseValidator<TUnion>
{
    private readonly IValueValidator<TValue> caseConstraints =
        ConstraintValidator<TValue>.FromTraits(
            unionCase.Traits,
            unionCase.Id,
            unionCase.TargetSchema
        );
    private readonly IValueValidator<TValue> valueValidator = ValidatorCompiler.Compile(
        unionCase.TargetSchema
    );

    public bool ValidateIfMatches(TUnion value, string path, List<SmithyValidationError> errors)
    {
        if (!unionCase.Matches(value))
        {
            return false;
        }

        var caseValue = unionCase.GetValue(value);
        if (caseValue is not null)
        {
            var casePath = JsonPointer.Append(path, unionCase.Name);
            caseConstraints.Validate(caseValue, casePath, errors);
            valueValidator.Validate(caseValue, casePath, errors);
        }

        return true;
    }
}

internal sealed class ConstraintValidator<T> : IValueValidator<T>
{
    private readonly IReadOnlyList<IConstraint<T>> constraints;

    private ConstraintValidator(IReadOnlyList<IConstraint<T>> constraints)
    {
        this.constraints = constraints;
    }

    public static IValueValidator<T> FromTraits(
        IReadOnlyDictionary<ShapeId, Trait> traits,
        ShapeId shapeId,
        Schema<T> target
    )
    {
        if (traits.Count == 0)
        {
            return NoOpValidator<T>.Instance;
        }

        var kind = target.Kind;
        List<IConstraint<T>> constraints = [];
        AddLengthConstraint(traits, shapeId, kind, target, constraints);
        AddRangeConstraint(traits, shapeId, kind, constraints);
        AddPatternConstraint(traits, shapeId, kind, constraints);
        return constraints.Count == 0
            ? NoOpValidator<T>.Instance
            : new ConstraintValidator<T>(constraints);
    }

    /// <summary>
    /// A constraint the shape's kind says should apply but that this validator cannot enforce is a
    /// bug in the validator, not in the model, so it fails loudly at compile time. Silently
    /// dropping it would leave the operation looking validated while accepting anything.
    /// </summary>
    private static InvalidOperationException Unenforceable(ShapeId constraint, ShapeId shapeId) =>
        new(
            $"Constraint trait '{constraint}' on '{shapeId}' cannot be enforced for CLR type "
                + $"'{typeof(T)}'."
        );

    public void Validate(T value, string path, List<SmithyValidationError> errors)
    {
        foreach (var constraint in constraints)
        {
            constraint.Validate(value, path, errors);
        }
    }

    private static void AddLengthConstraint(
        IReadOnlyDictionary<ShapeId, Trait> traits,
        ShapeId shapeId,
        ShapeKind kind,
        Schema<T> target,
        List<IConstraint<T>> constraints
    )
    {
        if (
            !traits.TryGetValue(ConstraintTraits.Length, out var trait)
            || kind
                is not (ShapeKind.String or ShapeKind.Blob)
                    and not (ShapeKind.List or ShapeKind.Set or ShapeKind.Map)
        )
        {
            return;
        }

        var length =
            LengthCompiler.For(target) ?? throw Unenforceable(ConstraintTraits.Length, shapeId);

        constraints.Add(
            new LengthConstraint<T>(
                shapeId,
                OptionalLong(trait.Value, "min"),
                OptionalLong(trait.Value, "max"),
                length
            )
        );
    }

    private static void AddRangeConstraint(
        IReadOnlyDictionary<ShapeId, Trait> traits,
        ShapeId shapeId,
        ShapeKind kind,
        List<IConstraint<T>> constraints
    )
    {
        if (
            !traits.TryGetValue(ConstraintTraits.Range, out var trait)
            || kind
                is not (ShapeKind.Byte or ShapeKind.Short or ShapeKind.Integer or ShapeKind.Long)
                    and not (ShapeKind.Float or ShapeKind.Double)
                    and not (ShapeKind.BigInteger or ShapeKind.BigDecimal)
        )
        {
            return;
        }

        var number =
            NumberAccessor<T>.Create() ?? throw Unenforceable(ConstraintTraits.Range, shapeId);

        constraints.Add(
            new RangeConstraint<T>(
                shapeId,
                OptionalDecimal(trait.Value, "min"),
                OptionalDecimal(trait.Value, "max"),
                number
            )
        );
    }

    private static void AddPatternConstraint(
        IReadOnlyDictionary<ShapeId, Trait> traits,
        ShapeId shapeId,
        ShapeKind kind,
        List<IConstraint<T>> constraints
    )
    {
        if (
            kind != ShapeKind.String
            || !traits.TryGetValue(ConstraintTraits.Pattern, out var trait)
            || trait.Value.Kind != DocumentKind.String
        )
        {
            return;
        }

        if (typeof(T) != typeof(string))
        {
            throw Unenforceable(ConstraintTraits.Pattern, shapeId);
        }

        var pattern = trait.Value.AsString();
        constraints.Add(new PatternConstraint<T>(shapeId, pattern, CompilePattern(pattern)));
    }

    /// <summary>
    /// Prefers the non-backtracking engine, which has no catastrophic-backtracking failure mode, so
    /// a hostile input cannot stall a request thread. Patterns using constructs it does not support
    /// (backreferences, lookaround) fall back to the backtracking engine under a timeout.
    /// </summary>
    private static Regex CompilePattern(string pattern)
    {
        try
        {
            return new Regex(pattern, RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
        }
        catch (NotSupportedException)
        {
            return new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
        }
    }

    private static long? OptionalLong(Document document, string memberName) =>
        OptionalDecimal(document, memberName) is { } value ? (long)value : null;

    private static decimal? OptionalDecimal(Document document, string memberName)
    {
        if (document.Kind != DocumentKind.Object)
        {
            return null;
        }

        var members = document.AsObject();
        return members.TryGetValue(memberName, out var member) && member.Kind == DocumentKind.Number
            ? member.AsNumber()
            : null;
    }
}

/// <summary>
/// Enum shapes are open in the generated types: an unrecognized value deserializes rather than
/// throwing, so that a client is not broken by a server that added a member. On a server that
/// openness has to stop at the handler, which is what this checks.
/// </summary>
internal sealed class StringEnumValidator<T>(StringEnumSchema<T> schema) : IValueValidator<T>
    where T : IStringEnumValue<T>
{
    public void Validate(T value, string path, List<SmithyValidationError> errors)
    {
        if (value?.Value is not { } text || schema.Contains(text))
        {
            return;
        }

        errors.Add(
            new SmithyValidationError(
                path,
                schema.Id,
                ConstraintTraits.Enum,
                ConstraintMessages.Failed(
                    path,
                    $"Member must satisfy enum value set: [{string.Join(", ", schema.Values)}]"
                )
            )
        );
    }
}

/// <inheritdoc cref="StringEnumValidator{T}" />
internal sealed class IntEnumValidator<T>(IntEnumSchema<T> schema) : IValueValidator<T>
    where T : struct, Enum
{
    public void Validate(T value, string path, List<SmithyValidationError> errors)
    {
        var number = schema.GetIntegerValue(value);
        if (schema.Contains(number))
        {
            return;
        }

        errors.Add(
            new SmithyValidationError(
                path,
                schema.Id,
                ConstraintTraits.Enum,
                ConstraintMessages.Failed(
                    path,
                    $"Member must satisfy enum value set: [{string.Join(", ", schema.Values)}]"
                )
            )
        );
    }
}

internal sealed class NoOpValidator<T> : IValueValidator<T>
{
    public static NoOpValidator<T> Instance { get; } = new();

    public void Validate(T value, string path, List<SmithyValidationError> errors) { }
}

internal interface IConstraint<in T>
{
    void Validate(T value, string path, List<SmithyValidationError> errors);
}

/// <summary>
/// Messages follow the wording Smithy's malformed-request tests assert, which is the de facto
/// contract for <c>smithy.framework#ValidationException</c> across implementations: a caller — or a
/// generic client — reading them gets the same text any other Smithy server would produce.
/// </summary>
internal static class ConstraintMessages
{
    public static string Bounds(long? min, long? max, string subject) =>
        (min, max) switch
        {
            ({ } low, { } high) => FormattableString.Invariant(
                $"{subject} between {low} and {high}, inclusive"
            ),
            ({ } low, null) => FormattableString.Invariant(
                $"{subject} greater than or equal to {low}"
            ),
            (null, { } high) => FormattableString.Invariant(
                $"{subject} less than or equal to {high}"
            ),
            _ => subject,
        };

    public static string Bounds(decimal? min, decimal? max, string subject) =>
        (min, max) switch
        {
            ({ } low, { } high) => FormattableString.Invariant(
                $"{subject} between {low} and {high}, inclusive"
            ),
            ({ } low, null) => FormattableString.Invariant(
                $"{subject} greater than or equal to {low}"
            ),
            (null, { } high) => FormattableString.Invariant(
                $"{subject} less than or equal to {high}"
            ),
            _ => subject,
        };

    public static string Failed(string path, string requirement) =>
        $"Value at '{path}' failed to satisfy constraint: {requirement}";

    public static string FailedWithLength(string path, int length, string requirement) =>
        FormattableString.Invariant(
            $"Value with length {length} at '{path}' failed to satisfy constraint: {requirement}"
        );
}

internal sealed class LengthConstraint<T>(
    ShapeId shapeId,
    long? min,
    long? max,
    Func<T, int> length
) : IConstraint<T>
{
    // One error per constraint, worded from the bounds the model declares rather than from the side
    // that was crossed: a value can only cross one, and the caller needs to be told both.
    private readonly string requirement = ConstraintMessages.Bounds(
        min,
        max,
        "Member must have length"
    );

    public void Validate(T value, string path, List<SmithyValidationError> errors)
    {
        var actual = length(value);
        if ((min is { } low && actual < low) || (max is { } high && actual > high))
        {
            errors.Add(
                new SmithyValidationError(
                    path,
                    shapeId,
                    ConstraintTraits.Length,
                    ConstraintMessages.FailedWithLength(path, actual, requirement)
                )
            );
        }
    }
}

internal sealed class RangeConstraint<T>(
    ShapeId shapeId,
    decimal? min,
    decimal? max,
    Func<T, decimal?> number
) : IConstraint<T>
{
    private readonly string requirement = ConstraintMessages.Bounds(min, max, "Member must be");

    public void Validate(T value, string path, List<SmithyValidationError> errors)
    {
        if (number(value) is not { } actual)
        {
            return;
        }

        if ((min is { } low && actual < low) || (max is { } high && actual > high))
        {
            errors.Add(
                new SmithyValidationError(
                    path,
                    shapeId,
                    ConstraintTraits.Range,
                    ConstraintMessages.Failed(path, requirement)
                )
            );
        }
    }
}

internal sealed class PatternConstraint<T>(ShapeId shapeId, string pattern, Regex regex)
    : IConstraint<T>
{
    public void Validate(T value, string path, List<SmithyValidationError> errors)
    {
        var text = (string)(object)value!;
        bool matches;
        try
        {
            matches = regex.IsMatch(text);
        }
        catch (RegexMatchTimeoutException)
        {
            // A pattern that cannot be decided within the timeout is reported as a violation
            // rather than escaping as a server fault: the input, not the server, is the problem.
            matches = false;
        }

        if (!matches)
        {
            errors.Add(
                new SmithyValidationError(
                    path,
                    shapeId,
                    ConstraintTraits.Pattern,
                    ConstraintMessages.Failed(
                        path,
                        $"Member must satisfy regular expression pattern: {pattern}"
                    )
                )
            );
        }
    }
}

/// <summary>
/// How to measure a value's length, taken from its schema rather than from its CLR type. Generated
/// collection types are ordinary records that wrap their storage — they implement no collection
/// interface — so only the schema knows how to enumerate one.
/// </summary>
internal static class LengthCompiler
{
    public static Func<T, int>? For<T>(Schema<T> schema) =>
        schema.Resolved.Accept(Instance) as Func<T, int>;

    private static readonly LengthVisitor Instance = new();

    private sealed class LengthVisitor : ISchemaVisitor<object?>
    {
        public object? VisitString(Schema<string> schema) =>
            // Smithy measures a string's length in Unicode code points, not UTF-16 units.
            (Func<string, int>)(value => value.EnumerateRunes().Count());

        public object? VisitBlob(Schema<byte[]> schema) =>
            (Func<byte[], int>)(value => value.Length);

        // A streaming blob reaches the handler unread, so its length is not knowable without
        // buffering the whole request.
        public object? VisitStreamingBlob(Schema<Stream> schema) => null;

        public object? VisitList<TCollection, TElement, TBuilder>(
            IListSchema<TCollection, TElement, TBuilder> schema
        ) => (Func<TCollection, int>)(value => Count(schema.GetElements(value)));

        public object? VisitMap<TDictionary, TValue, TBuilder>(
            IMapSchema<TDictionary, TValue, TBuilder> schema
        ) => (Func<TDictionary, int>)(value => Count(schema.GetEntries(value)));

        public object? VisitBoolean(Schema<bool> schema) => null;

        public object? VisitByte(Schema<sbyte> schema) => null;

        public object? VisitShort(Schema<short> schema) => null;

        public object? VisitInteger(Schema<int> schema) => null;

        public object? VisitLong(Schema<long> schema) => null;

        public object? VisitFloat(Schema<float> schema) => null;

        public object? VisitDouble(Schema<double> schema) => null;

        public object? VisitBigInteger(Schema<BigInteger> schema) => null;

        public object? VisitBigDecimal(Schema<decimal> schema) => null;

        public object? VisitTimestamp(Schema<DateTimeOffset> schema) => null;

        public object? VisitDocument(Schema<Document> schema) => null;

        public object? VisitNullable<T>(NullableSchema<T> schema)
            where T : struct => null;

        public object? VisitEventStream<TEvent>(EventStreamSchema<TEvent> schema) => null;

        public object? VisitStruct<T, TBuilder>(IStructSchema<T, TBuilder> schema) => null;

        public object? VisitUnion<T>(IUnionSchema<T> schema) => null;

        public object? VisitStringEnum<T>(StringEnumSchema<T> schema)
            where T : IStringEnumValue<T> => null;

        public object? VisitIntEnum<T>(IntEnumSchema<T> schema)
            where T : struct, Enum => null;

        private static int Count<TItem>(IEnumerable<TItem> items) =>
            items is ICollection<TItem> collection ? collection.Count : items.Count();
    }
}

internal static class NumberAccessor<T>
{
    // decimal cannot hold the whole double, float or BigInteger range. Magnitudes beyond it are
    // clamped rather than converted, so an out-of-range value compares as out of range instead of
    // throwing OverflowException on the request path. Any bound a model can express is far inside
    // these limits, so clamping never changes a verdict.
    private const double DecimalUpperBound = 7.9e28;
    private const double DecimalLowerBound = -7.9e28;

    /// <summary>
    /// Builds a reader for the CLR type behind a numeric shape. An optional member wraps its value
    /// in <see cref="Nullable{T}"/>, so the underlying type — not <c>typeof(T)</c> — selects the
    /// reader; a null value yields null and is left to the <c>@required</c> check.
    /// </summary>
    public static Func<T, decimal?>? Create()
    {
        var read = CreateReader(Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T));
        return read is null ? null : value => value is null ? null : read(value);
    }

    private static Func<object, decimal?>? CreateReader(Type type)
    {
        if (type == typeof(sbyte))
        {
            return static value => (sbyte)value;
        }

        if (type == typeof(short))
        {
            return static value => (short)value;
        }

        if (type == typeof(int))
        {
            return static value => (int)value;
        }

        if (type == typeof(long))
        {
            return static value => (long)value;
        }

        if (type == typeof(byte))
        {
            return static value => (byte)value;
        }

        if (type == typeof(ushort))
        {
            return static value => (ushort)value;
        }

        if (type == typeof(uint))
        {
            return static value => (uint)value;
        }

        if (type == typeof(ulong))
        {
            return static value => (ulong)value;
        }

        if (type == typeof(float))
        {
            return static value => FromDouble((float)value);
        }

        if (type == typeof(double))
        {
            return static value => FromDouble((double)value);
        }

        if (type == typeof(decimal))
        {
            return static value => (decimal)value;
        }

        if (type == typeof(BigInteger))
        {
            return static value =>
            {
                var number = (BigInteger)value;
                return number >= new BigInteger(decimal.MinValue)
                    ? number <= new BigInteger(decimal.MaxValue)
                        ? (decimal)number
                        : decimal.MaxValue
                    : decimal.MinValue;
            };
        }

        return null;
    }

    private static decimal? FromDouble(double number) =>
        !double.IsFinite(number) ? null
        : number >= DecimalUpperBound ? decimal.MaxValue
        : number <= DecimalLowerBound ? decimal.MinValue
        : (decimal)number;
}
