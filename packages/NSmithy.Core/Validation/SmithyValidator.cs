using System.Collections;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
using NSmithy.Core.Serde;

namespace NSmithy.Core.Validation;

public interface ISmithyValidator<in T>
{
    void Validate(T value);

    IReadOnlyList<SmithyValidationError> GetErrors(T value);
}

public sealed record SmithyValidationError(
    string Path,
    ShapeId ShapeId,
    ShapeId ConstraintId,
    string Message
);

public sealed class SmithyValidationException : Exception
{
    public SmithyValidationException(IReadOnlyList<SmithyValidationError> errors)
        : base(CreateMessage(errors))
    {
        Errors = new ReadOnlyCollection<SmithyValidationError>(errors.ToArray());
    }

    public IReadOnlyList<SmithyValidationError> Errors { get; }

    private static string CreateMessage(IReadOnlyList<SmithyValidationError> errors) =>
        errors.Count == 1
            ? errors[0].Message
            : string.Create(
                CultureInfo.InvariantCulture,
                $"Smithy validation failed with {errors.Count} error(s)."
            );
}

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

internal sealed class SchemaValidator<T>(IValueValidator<T> validator) : ISmithyValidator<T>
{
    public void Validate(T value)
    {
        var errors = GetErrors(value);
        if (errors.Count > 0)
        {
            throw new SmithyValidationException(errors);
        }
    }

    public IReadOnlyList<SmithyValidationError> GetErrors(T value)
    {
        List<SmithyValidationError> errors = [];
        validator.Validate(value, "$", errors);
        return errors;
    }
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
        var constraints = ConstraintValidator<T>.FromTraits(schema.Traits, schema.Id);
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
            where T : IStringEnumValue<T> => NoOpValidator<T>.Instance;

        public object VisitIntEnum<T>(IntEnumSchema<T> schema)
            where T : struct, Enum => NoOpValidator<T>.Instance;
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
            where T : IStringEnumValue<T> => false;

        public bool VisitIntEnum<T>(IntEnumSchema<T> schema)
            where T : struct, Enum => false;
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

    internal static MemberValueValidator<TValue> CompileMember<TValue>(
        IMemberSchema member,
        Schema<TValue> target
    )
    {
        return new MemberValueValidator<TValue>(
            ConstraintValidator<TValue>.FromTraits(member.MemberTraits, member.Id),
            Compile(target)
        );
    }

    internal static MemberValueValidator<TValue> CompileMember<TValue>(
        ICollectionMemberSchema<TValue> member
    ) => CompileMember(member, member.TargetSchema);
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

internal sealed class DeferredValidator<T>(Schema<T> schema) : IValueValidator<T>
{
    private readonly Lazy<IValueValidator<T>> body = new(() =>
        ValidatorCompiler.CompileBody(schema)
    );

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
        var valueValidator = ValidatorCompiler.CompileMember(member, member.TargetSchema);
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
        var memberPath = path + "." + member.Name;
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
                        $"Required member '{member.Name}' must not be null."
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
    private readonly MemberValueValidator<TElement> elementValidator;

    public ListValidator(IListSchema<TCollection, TElement> schema)
    {
        this.schema = schema;
        var shape = (Schema<TCollection>)schema;
        shapeId = shape.Id;
        uniqueItems = shape.HasTrait(ConstraintTraits.UniqueItems);
        elementValidator = ValidatorCompiler.CompileMember(schema.TypedElementMember);
    }

    public void Validate(TCollection value, string path, List<SmithyValidationError> errors)
    {
        if (value is null)
        {
            return;
        }

        HashSet<TElement>? seen = uniqueItems ? [] : null;
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
                        $"Value at '{path}' has duplicate items."
                    )
                );
                seen = null;
            }

            if (element is not null)
            {
                elementValidator.Validate(element, $"{path}[{index}]", errors);
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
            keyValidator.Validate(entry.Key, AppendKey(path, entry.Key), errors);
            if (entry.Value is not null)
            {
                valueValidator.Validate(entry.Value, AppendKey(path, entry.Key), errors);
            }
        }
    }

    private static string AppendKey(string path, string key) =>
        path
        + "[\""
        + key.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
        + "\"]";
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
        ConstraintValidator<TValue>.FromTraits(unionCase.Traits, unionCase.Target.Id);
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
            var casePath = path + "." + unionCase.Name;
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
        ShapeId shapeId
    )
    {
        List<IConstraint<T>> constraints = [];
        AddLengthConstraint(traits, shapeId, constraints);
        AddRangeConstraint(traits, shapeId, constraints);
        AddPatternConstraint(traits, shapeId, constraints);
        return constraints.Count == 0
            ? NoOpValidator<T>.Instance
            : new ConstraintValidator<T>(constraints);
    }

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
        List<IConstraint<T>> constraints
    )
    {
        if (!traits.TryGetValue(ConstraintTraits.Length, out var trait))
        {
            return;
        }

        var length = LengthAccessor<T>.Create();
        if (length is null)
        {
            return;
        }

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
        List<IConstraint<T>> constraints
    )
    {
        if (!traits.TryGetValue(ConstraintTraits.Range, out var trait))
        {
            return;
        }

        var number = NumberAccessor<T>.Create();
        if (number is null)
        {
            return;
        }

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
        List<IConstraint<T>> constraints
    )
    {
        if (
            typeof(T) != typeof(string)
            || !traits.TryGetValue(ConstraintTraits.Pattern, out var trait)
            || trait.Value.Kind != DocumentKind.String
        )
        {
            return;
        }

        var pattern = trait.Value.AsString();
        var regex = new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
        constraints.Add(new PatternConstraint<T>(shapeId, pattern, regex));
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

internal sealed class NoOpValidator<T> : IValueValidator<T>
{
    public static NoOpValidator<T> Instance { get; } = new();

    public void Validate(T value, string path, List<SmithyValidationError> errors) { }
}

internal interface IConstraint<in T>
{
    void Validate(T value, string path, List<SmithyValidationError> errors);
}

internal sealed class LengthConstraint<T>(
    ShapeId shapeId,
    long? min,
    long? max,
    Func<T, int> length
) : IConstraint<T>
{
    public void Validate(T value, string path, List<SmithyValidationError> errors)
    {
        var actual = length(value);
        if (min is { } minValue && actual < minValue)
        {
            errors.Add(
                new SmithyValidationError(
                    path,
                    shapeId,
                    ConstraintTraits.Length,
                    $"Value at '{path}' length {actual} is less than minimum {minValue}."
                )
            );
        }

        if (max is { } maxValue && actual > maxValue)
        {
            errors.Add(
                new SmithyValidationError(
                    path,
                    shapeId,
                    ConstraintTraits.Length,
                    $"Value at '{path}' length {actual} is greater than maximum {maxValue}."
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
    public void Validate(T value, string path, List<SmithyValidationError> errors)
    {
        if (number(value) is not { } actual)
        {
            return;
        }

        if (min is { } minValue && actual < minValue)
        {
            errors.Add(
                new SmithyValidationError(
                    path,
                    shapeId,
                    ConstraintTraits.Range,
                    $"Value at '{path}' {actual} is less than minimum {minValue}."
                )
            );
        }

        if (max is { } maxValue && actual > maxValue)
        {
            errors.Add(
                new SmithyValidationError(
                    path,
                    shapeId,
                    ConstraintTraits.Range,
                    $"Value at '{path}' {actual} is greater than maximum {maxValue}."
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
        if (!regex.IsMatch(text))
        {
            errors.Add(
                new SmithyValidationError(
                    path,
                    shapeId,
                    ConstraintTraits.Pattern,
                    $"Value at '{path}' does not match pattern '{pattern}'."
                )
            );
        }
    }
}

internal static class LengthAccessor<T>
{
    public static Func<T, int>? Create()
    {
        if (typeof(T) == typeof(string))
        {
            return static value => ((string)(object)value!).EnumerateRunes().Count();
        }

        if (typeof(T) == typeof(byte[]))
        {
            return static value => ((byte[])(object)value!).Length;
        }

        if (typeof(ICollection).IsAssignableFrom(typeof(T)))
        {
            return static value => ((ICollection)value!).Count;
        }

        if (typeof(IEnumerable).IsAssignableFrom(typeof(T)))
        {
            return static value =>
            {
                var count = 0;
                foreach (var _ in (IEnumerable)value!)
                {
                    count++;
                }

                return count;
            };
        }

        return null;
    }
}

internal static class NumberAccessor<T>
{
    public static Func<T, decimal?>? Create()
    {
        if (typeof(T) == typeof(sbyte))
        {
            return static value => (sbyte)(object)value!;
        }

        if (typeof(T) == typeof(short))
        {
            return static value => (short)(object)value!;
        }

        if (typeof(T) == typeof(int))
        {
            return static value => (int)(object)value!;
        }

        if (typeof(T) == typeof(long))
        {
            return static value => (long)(object)value!;
        }

        if (typeof(T) == typeof(byte))
        {
            return static value => (byte)(object)value!;
        }

        if (typeof(T) == typeof(ushort))
        {
            return static value => (ushort)(object)value!;
        }

        if (typeof(T) == typeof(uint))
        {
            return static value => (uint)(object)value!;
        }

        if (typeof(T) == typeof(ulong))
        {
            return static value => (ulong)(object)value!;
        }

        if (typeof(T) == typeof(float))
        {
            return static value =>
            {
                var number = (float)(object)value!;
                return float.IsFinite(number) ? (decimal)number : null;
            };
        }

        if (typeof(T) == typeof(double))
        {
            return static value =>
            {
                var number = (double)(object)value!;
                return double.IsFinite(number) ? (decimal)number : null;
            };
        }

        if (typeof(T) == typeof(decimal))
        {
            return static value => (decimal)(object)value!;
        }

        if (typeof(T) == typeof(BigInteger))
        {
            return static value =>
            {
                var number = (BigInteger)(object)value!;
                return
                    number >= new BigInteger(decimal.MinValue)
                    && number <= new BigInteger(decimal.MaxValue)
                    ? (decimal)number
                    : null;
            };
        }

        return null;
    }
}
