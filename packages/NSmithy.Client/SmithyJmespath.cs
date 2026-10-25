using System.ComponentModel;

namespace NSmithy.Client;

/// <summary>
/// JMESPath semantics for generated waiter acceptors, which compile JMESPath to C# over the
/// generated types. Strings, booleans, numbers (as <see cref="double"/>), structures, lists, and
/// maps follow the specification's truthiness, comparison, and function rules; a value of the
/// wrong type for a function or comparison yields null.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class SmithyJmespath
{
    /// <summary>A string is truthy when it is not empty.</summary>
    public static bool IsTruthy(string? value) => !string.IsNullOrEmpty(value);

    /// <summary>A boolean is truthy when it is true.</summary>
    public static bool IsTruthy(bool? value) => value == true;

    /// <summary>A number is truthy when it is present.</summary>
    public static bool IsTruthy(double? value) => value is not null;

    /// <summary>A list or map is truthy when it is not empty.</summary>
    public static bool IsTruthy<T>(IEnumerable<T>? value) => value is not null && value.Any();

    /// <summary>A structure is truthy when it is present.</summary>
    public static bool IsTruthy(object? value) => value is not null;

    /// <summary><c>a &amp;&amp; b</c>: <paramref name="right"/> when <paramref name="left"/> is truthy, else <paramref name="left"/>.</summary>
    public static bool? And(bool? left, bool? right) => IsTruthy(left) ? right : left;

    /// <inheritdoc cref="And(bool?, bool?)"/>
    public static string? And(string? left, string? right) => IsTruthy(left) ? right : left;

    /// <inheritdoc cref="And(bool?, bool?)"/>
    public static double? And(double? left, double? right) => IsTruthy(left) ? right : left;

    /// <summary><c>a || b</c>: <paramref name="left"/> when it is truthy, else <paramref name="right"/>.</summary>
    public static bool? Or(bool? left, bool? right) => IsTruthy(left) ? left : right;

    /// <inheritdoc cref="Or(bool?, bool?)"/>
    public static string? Or(string? left, string? right) => IsTruthy(left) ? left : right;

    /// <inheritdoc cref="Or(bool?, bool?)"/>
    public static double? Or(double? left, double? right) => IsTruthy(left) ? left : right;

    /// <summary><c>length</c> of a string: its number of code points.</summary>
    public static double? Length(string? value) =>
        value is null ? null : value.EnumerateRunes().Count();

    /// <summary><c>length</c> of a list or map: its number of items.</summary>
    public static double? Length<T>(IEnumerable<T>? value) => value?.Count();

    /// <summary><c>contains</c> on a string: whether it contains <paramref name="search"/>.</summary>
    public static bool? Contains(string? subject, string? search) =>
        subject is null || search is null
            ? null
            : subject.Contains(search, StringComparison.Ordinal);

    /// <summary><c>contains</c> on a list: whether an item equals <paramref name="search"/>.</summary>
    public static bool? Contains<T>(IEnumerable<T>? subject, T search) =>
        subject?.Contains(search, EqualityComparer<T>.Default);

    /// <summary><c>&lt;</c>: null unless both sides are numbers.</summary>
    public static bool? LessThan(double? left, double? right) =>
        left is null || right is null ? null : left < right;

    /// <summary><c>&lt;=</c>: null unless both sides are numbers.</summary>
    public static bool? LessThanOrEqual(double? left, double? right) =>
        left is null || right is null ? null : left <= right;

    /// <summary><c>&gt;</c>: null unless both sides are numbers.</summary>
    public static bool? GreaterThan(double? left, double? right) =>
        left is null || right is null ? null : left > right;

    /// <summary><c>&gt;=</c>: null unless both sides are numbers.</summary>
    public static bool? GreaterThanOrEqual(double? left, double? right) =>
        left is null || right is null ? null : left >= right;

    /// <summary>
    /// The <c>allStringEquals</c> comparator: a non-empty list whose items all equal
    /// <paramref name="expected"/>.
    /// </summary>
    public static bool AllStringEquals(IEnumerable<string?>? values, string expected)
    {
        if (values is null)
        {
            return false;
        }
        var any = false;
        foreach (var value in values)
        {
            if (!string.Equals(value, expected, StringComparison.Ordinal))
            {
                return false;
            }
            any = true;
        }
        return any;
    }

    /// <summary>
    /// The <c>anyStringEquals</c> comparator: a list with an item equal to
    /// <paramref name="expected"/>.
    /// </summary>
    public static bool AnyStringEquals(IEnumerable<string?>? values, string expected) =>
        values is not null
        && values.Any(value => string.Equals(value, expected, StringComparison.Ordinal));
}
