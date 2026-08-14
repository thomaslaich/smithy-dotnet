using System.Globalization;
using System.Text.RegularExpressions;

namespace Bench.Stacks.MinimalApi;

/// <summary>
/// Hand-written equivalents of the constraint traits in <c>bench.smithy</c>.
/// </summary>
/// <remarks>
/// NSmithy derives constraint validation from the model and runs it on every
/// request, so a baseline that skipped it would do strictly less work than the
/// stack it is the ceiling for, most visibly on <c>create-order-large</c>, where
/// 6,400 line items each carry a pattern and two range constraints. The messages
/// were copied from NSmithy's actual responses rather than guessed; the three
/// constraint kinds word themselves differently. Nested member paths
/// (<c>/lines/3/itemId</c>) are unverified, no corpus scenario violates one.
/// </remarks>
public static class BenchValidation
{
    private const string ItemIdPatternText = "^item-[0-9]{5}$";

    private static readonly Regex ItemIdRegex = new(
        ItemIdPatternText,
        RegexOptions.Compiled | RegexOptions.CultureInvariant
    );

    /// <summary>Checks a <c>@pattern</c> constraint. Returns null when satisfied.</summary>
    public static string? ItemId(string value, string path) =>
        ItemIdRegex.IsMatch(value)
            ? null
            : $"Value at '{path}' failed to satisfy constraint: "
                + $"Member must satisfy regular expression pattern: {ItemIdPatternText}";

    /// <summary>Checks a <c>@range</c> constraint. Returns null when satisfied.</summary>
    public static string? Range(int value, int min, int max, string path) =>
        value >= min && value <= max
            ? null
            : $"Value at '{path}' failed to satisfy constraint: "
                + string.Create(
                    CultureInfo.InvariantCulture,
                    $"Member must be between {min} and {max}, inclusive"
                );

    /// <summary>Checks a <c>@length</c> constraint. Returns null when satisfied.</summary>
    public static string? Length(string value, int min, int max, string path) =>
        value.Length >= min && value.Length <= max
            ? null
            : string.Create(
                CultureInfo.InvariantCulture,
                $"Value with length {value.Length} at '{path}' failed to satisfy constraint: "
                    + $"Member must have length between {min} and {max}, inclusive"
            );

    /// <summary>
    /// Adds a failure to a list that is only allocated once something fails, so a
    /// valid request pays nothing for the collection itself.
    /// </summary>
    public static void Collect(
        ref List<ValidationExceptionField>? failures,
        string? message,
        string path
    )
    {
        if (message is null)
            return;

        (failures ??= []).Add(new ValidationExceptionField { Path = path, Message = message });
    }

    /// <summary>Builds the response body, matching Smithy's aggregate wording.</summary>
    public static ValidationExceptionResponse Build(List<ValidationExceptionField> failures)
    {
        ArgumentNullException.ThrowIfNull(failures);

        var noun = failures.Count == 1 ? "error" : "errors";
        var detail = string.Join("; ", failures.Select(f => f.Message));

        return new ValidationExceptionResponse
        {
            Message = string.Create(
                CultureInfo.InvariantCulture,
                $"{failures.Count} validation {noun} detected. {detail}"
            ),
            FieldList = failures,
        };
    }
}

/// <summary>The <c>smithy.framework#ValidationException</c> wire shape.</summary>
public sealed class ValidationExceptionResponse
{
    public required string Message { get; init; }
    public required IReadOnlyList<ValidationExceptionField> FieldList { get; init; }
}

/// <summary>One constraint failure within a <see cref="ValidationExceptionResponse"/>.</summary>
public sealed class ValidationExceptionField
{
    public required string Path { get; init; }
    public required string Message { get; init; }
}
