using System.Globalization;
using System.Text;
using System.Text.Json;

namespace RpcV2Json.Conformance;

/// <summary>
/// Compares JSON bodies by structure: property order and whitespace are ignored, and numbers are
/// compared by value so that <c>1.0</c> and <c>1</c> match.
/// </summary>
internal static class JsonAssert
{
    public static void AreStructurallyEqual(string expected, byte[] actual)
    {
        var actualText = Encoding.UTF8.GetString(actual);
        using var expectedDocument = Parse(expected, "expected");
        using var actualDocument = Parse(actualText, "actual");
        var mismatch = Compare(expectedDocument.RootElement, actualDocument.RootElement, "$");
        Assert.True(
            mismatch is null,
            $"JSON body mismatch at {mismatch}.\nExpected: {expected}\nActual:   {actualText}"
        );
    }

    private static JsonDocument Parse(string text, string side)
    {
        try
        {
            return JsonDocument.Parse(text);
        }
        catch (JsonException exception)
        {
            throw new Xunit.Sdk.XunitException(
                $"The {side} body is not valid JSON ({exception.Message}): {text}"
            );
        }
    }

    private static string? Compare(JsonElement expected, JsonElement actual, string path)
    {
        if (expected.ValueKind != actual.ValueKind)
            return $"{path} (expected {expected.ValueKind}, got {actual.ValueKind})";

        switch (expected.ValueKind)
        {
            case JsonValueKind.Object:
                var actualProperties = actual
                    .EnumerateObject()
                    .ToDictionary(property => property.Name, property => property.Value);
                var expectedCount = 0;
                foreach (var property in expected.EnumerateObject())
                {
                    expectedCount++;
                    if (!actualProperties.TryGetValue(property.Name, out var value))
                        return $"{path}.{property.Name} (missing)";
                    var nested = Compare(property.Value, value, $"{path}.{property.Name}");
                    if (nested is not null)
                        return nested;
                }

                return expectedCount == actualProperties.Count
                    ? null
                    : $"{path} (unexpected properties: {string.Join(", ", actualProperties.Keys.Except(expected.EnumerateObject().Select(p => p.Name)))})";
            case JsonValueKind.Array:
                var expectedItems = expected.EnumerateArray().ToList();
                var actualItems = actual.EnumerateArray().ToList();
                if (expectedItems.Count != actualItems.Count)
                    return $"{path} (expected {expectedItems.Count} items, got {actualItems.Count})";
                for (var index = 0; index < expectedItems.Count; index++)
                {
                    var nested = Compare(
                        expectedItems[index],
                        actualItems[index],
                        $"{path}[{index}]"
                    );
                    if (nested is not null)
                        return nested;
                }

                return null;
            case JsonValueKind.Number:
                return NumbersEqual(expected.GetRawText(), actual.GetRawText())
                    ? null
                    : $"{path} (expected {expected.GetRawText()}, got {actual.GetRawText()})";
            case JsonValueKind.String:
                return expected.GetString() == actual.GetString()
                    ? null
                    : $"{path} (expected \"{expected.GetString()}\", got \"{actual.GetString()}\")";
            default:
                return null;
        }
    }

    private static bool NumbersEqual(string expected, string actual) =>
        expected == actual
        || (
            decimal.TryParse(expected, NumberStyles.Float, CultureInfo.InvariantCulture, out var e)
            && decimal.TryParse(actual, NumberStyles.Float, CultureInfo.InvariantCulture, out var a)
            && e == a
        )
        || double.Parse(expected, CultureInfo.InvariantCulture)
            == double.Parse(actual, CultureInfo.InvariantCulture);
}
