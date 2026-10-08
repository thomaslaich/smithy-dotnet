using System.Collections.Frozen;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Numerics;
using System.Text;
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
    /// Builds a validator for the schema, or returns null when nothing reachable from it carries a
    /// validation constraint, so callers can skip validation entirely.
    /// </summary>
    public static ISmithyValidator<T>? FromSchema<T>(Schema<T> schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        var root = new ValidatorPlans().ForRoot(schema);
        return root.Needs ? new SchemaValidator<T>(schema, root) : null;
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

/// <summary>
/// An aggregate the validator has descended into, linked to the aggregate holding it. The pointer
/// string is only rendered when a value inside it fails a check, so a valid request builds none.
/// </summary>
internal sealed class PathNode(PathNode? parent, string? name, int index)
{
    public string Render()
    {
        var path = parent?.Render() ?? JsonPointer.Root;
        return name is not null ? JsonPointer.Append(path, name)
            : index >= 0 ? JsonPointer.Append(path, index)
            : path;
    }
}

/// <summary>
/// Where a value sits: its container's node, plus the member name or element index within it. A
/// value with neither is the container itself, such as the root or a map key.
/// </summary>
internal readonly struct ValuePath(PathNode? container, string? name = null, int index = -1)
{
    public string Render() => new PathNode(container, name, index).Render();

    /// <summary>The node for an aggregate the validator descends into at this path.</summary>
    public PathNode ToNode() => new(container, name, index);
}

/// <summary>
/// Validates a value by serializing it into a <see cref="ShapeValidator"/>, whose plan checks
/// each member's constraints as the value is written.
/// </summary>
internal sealed class SchemaValidator<T>(Schema<T> schema, ValidatorMemberPlan root)
    : ISmithyValidator<T>
{
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
        var serializer = new ShapeValidator(root, errors);
        schema.Write(MemberIndex.Root, value, ref serializer);
        return new ReadOnlyCollection<SmithyValidationError>(errors);
    }
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
            || traits.ContainsKey(Enum)
        );
}

/// <summary>
/// Messages follow the wording Smithy's malformed-request tests assert, which is the de facto
/// contract for <c>smithy.framework#ValidationException</c> across implementations: a caller — or a
/// generic client — reading them gets the same text any other Smithy server would produce.
/// </summary>
internal static class ConstraintMessages
{
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

    public static SmithyValidationError EnumMembership(
        string path,
        ShapeId shapeId,
        IEnumerable<string> published
    ) =>
        new(
            path,
            shapeId,
            ConstraintTraits.Enum,
            Failed(path, $"Member must satisfy enum value set: [{string.Join(", ", published)}]")
        );
}

/// <summary>
/// The constraint traits declared on one edge (a member, or a shape itself), compiled for the kind
/// of value they constrain. Errors name <see cref="ShapeId"/>: the member for a member's own
/// traits, the shape for the shape's.
/// </summary>
internal sealed class EdgeConstraints
{
    private readonly ShapeId shapeId;
    private readonly (long? Min, long? Max, string Requirement)? length;
    private readonly (decimal? Min, decimal? Max, string Requirement)? range;
    private readonly (string Pattern, Regex Regex)? pattern;
    private readonly (FrozenSet<string> Values, IReadOnlyList<string> Published)? enumTrait;

    private EdgeConstraints(
        ShapeId shapeId,
        (long? Min, long? Max, string Requirement)? length,
        (decimal? Min, decimal? Max, string Requirement)? range,
        (string Pattern, Regex Regex)? pattern,
        (FrozenSet<string> Values, IReadOnlyList<string> Published)? enumTrait
    )
    {
        this.shapeId = shapeId;
        this.length = length;
        this.range = range;
        this.pattern = pattern;
        this.enumTrait = enumTrait;
    }

    /// <summary>The constraints <paramref name="traits"/> declare for a value of <paramref name="target"/>, or null.</summary>
    public static EdgeConstraints? From(
        IReadOnlyDictionary<ShapeId, Trait> traits,
        ShapeId shapeId,
        Schema target
    )
    {
        if (traits.Count == 0)
        {
            return null;
        }

        var resolved = target.Resolved;
        var kind = resolved.Kind;

        (long? Min, long? Max, string Requirement)? length = null;
        // A @streaming blob reaches the handler unread, so its length is not knowable without
        // buffering the whole request; it is skipped rather than failing the build.
        if (
            traits.TryGetValue(ConstraintTraits.Length, out var lengthTrait)
            && kind
                is ShapeKind.String
                    or ShapeKind.Blob
                    or ShapeKind.List
                    or ShapeKind.Set
                    or ShapeKind.Map
            && resolved is not StreamingBlobSchema
        )
        {
            var min = Bound(lengthTrait.Value, "min");
            var max = Bound(lengthTrait.Value, "max");
            length = (
                (long?)min,
                (long?)max,
                ConstraintMessages.Bounds(min, max, "Member must have length")
            );
        }

        (decimal? Min, decimal? Max, string Requirement)? range = null;
        if (
            traits.TryGetValue(ConstraintTraits.Range, out var rangeTrait)
            && kind
                is ShapeKind.Byte
                    or ShapeKind.Short
                    or ShapeKind.Integer
                    or ShapeKind.Long
                    or ShapeKind.Float
                    or ShapeKind.Double
                    or ShapeKind.BigInteger
                    or ShapeKind.BigDecimal
        )
        {
            var min = Bound(rangeTrait.Value, "min");
            var max = Bound(rangeTrait.Value, "max");
            range = (min, max, ConstraintMessages.Bounds(min, max, "Member must be"));
        }

        (string Pattern, Regex Regex)? pattern = null;
        if (
            kind == ShapeKind.String
            && traits.TryGetValue(ConstraintTraits.Pattern, out var patternTrait)
            && patternTrait.Value.Kind == DocumentKind.String
        )
        {
            var text = patternTrait.Value.AsString();
            pattern = (text, CompilePattern(text));
        }

        var enumTrait = kind == ShapeKind.String ? EnumTrait(traits) : null;

        return length is null && range is null && pattern is null && enumTrait is null
            ? null
            : new EdgeConstraints(shapeId, length, range, pattern, enumTrait);
    }

    public void CheckLength(int actual, ValuePath path, List<SmithyValidationError> errors)
    {
        // One error per constraint, worded from the bounds the model declares rather than from the
        // side that was crossed: a value can only cross one, and the caller needs to be told both.
        if (
            length is { } bounds
            && (
                (bounds.Min is { } low && actual < low) || (bounds.Max is { } high && actual > high)
            )
        )
        {
            var rendered = path.Render();
            errors.Add(
                new SmithyValidationError(
                    rendered,
                    shapeId,
                    ConstraintTraits.Length,
                    ConstraintMessages.FailedWithLength(rendered, actual, bounds.Requirement)
                )
            );
        }
    }

    public void CheckRange(decimal? actual, ValuePath path, List<SmithyValidationError> errors)
    {
        if (
            range is { } bounds
            && actual is { } value
            && ((bounds.Min is { } low && value < low) || (bounds.Max is { } high && value > high))
        )
        {
            var rendered = path.Render();
            errors.Add(
                new SmithyValidationError(
                    rendered,
                    shapeId,
                    ConstraintTraits.Range,
                    ConstraintMessages.Failed(rendered, bounds.Requirement)
                )
            );
        }
    }

    public void CheckString(string value, ValuePath path, List<SmithyValidationError> errors)
    {
        // Smithy measures a string's length in Unicode code points, not UTF-16 units.
        if (length is not null)
        {
            var codePoints = 0;
            foreach (var _ in value.EnumerateRunes())
            {
                codePoints++;
            }

            CheckLength(codePoints, path, errors);
        }

        if (pattern is { } expected && !Matches(expected.Regex, value))
        {
            var rendered = path.Render();
            errors.Add(
                new SmithyValidationError(
                    rendered,
                    shapeId,
                    ConstraintTraits.Pattern,
                    ConstraintMessages.Failed(
                        rendered,
                        $"Member must satisfy regular expression pattern: {expected.Pattern}"
                    )
                )
            );
        }

        if (enumTrait is { } set && !set.Values.Contains(value))
        {
            errors.Add(ConstraintMessages.EnumMembership(path.Render(), shapeId, set.Published));
        }
    }

    // A pattern that cannot be decided within the timeout is reported as a violation rather than
    // escaping as a server fault: the input, not the server, is the problem.
    private static bool Matches(Regex regex, string value)
    {
        try
        {
            return regex.IsMatch(value);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
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

    /// <summary>
    /// The deprecated <c>@enum</c> trait on a string shape, which carries its value set in the
    /// trait rather than on an enum shape. A value tagged internal is accepted but not listed back.
    /// </summary>
    private static (FrozenSet<string>, IReadOnlyList<string>)? EnumTrait(
        IReadOnlyDictionary<ShapeId, Trait> traits
    )
    {
        if (
            !traits.TryGetValue(ConstraintTraits.Enum, out var trait)
            || trait.Value.Kind != DocumentKind.Array
        )
        {
            return null;
        }

        List<string> values = [];
        List<string> published = [];
        foreach (var entry in trait.Value.AsArray())
        {
            if (
                entry.Kind != DocumentKind.Object
                || !entry.AsObject().TryGetValue("value", out var value)
                || value.Kind != DocumentKind.String
            )
            {
                continue;
            }

            values.Add(value.AsString());
            var isInternal =
                entry.AsObject().TryGetValue("tags", out var tags)
                && tags.Kind == DocumentKind.Array
                && tags.AsArray()
                    .Any(tag => tag.Kind == DocumentKind.String && tag.AsString() == "internal");
            if (!isInternal)
            {
                published.Add(value.AsString());
            }
        }

        return values.Count == 0 ? null : (values.ToFrozenSet(StringComparer.Ordinal), published);
    }

    private static decimal? Bound(Document document, string memberName) =>
        document.Kind == DocumentKind.Object
        && document.AsObject().TryGetValue(memberName, out var member)
        && member.Kind == DocumentKind.Number
            ? member.AsNumber()
            : null;
}
