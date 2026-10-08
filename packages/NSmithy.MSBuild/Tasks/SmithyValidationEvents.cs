using System.Globalization;
using System.Text;

namespace NSmithy.Contracts;

/// <summary>Reads the validation events written by <c>smithy validate --format csv</c>.</summary>
internal static class SmithyValidationEvents
{
    /// <summary>
    /// Parses the CSV (RFC 4180, with a header row). Columns are looked up by name, and a
    /// location of <c>N/A</c> or an empty file is reported as no location.
    /// </summary>
    public static IReadOnlyList<SmithyValidationEvent> ParseCsv(string csv)
    {
        var records = ReadRecords(csv);
        if (records.Count == 0)
            return [];

        var header = records[0];
        string Field(List<string> record, string name)
        {
            var index = header.IndexOf(name);
            return index >= 0 && index < record.Count ? record[index] : "";
        }

        var events = new List<SmithyValidationEvent>();
        foreach (var record in records.Skip(1))
        {
            var file = Field(record, "file");
            var hasLocation = file.Length > 0 && file != "N/A";
            events.Add(
                new SmithyValidationEvent(
                    Severity: Field(record, "severity"),
                    EventId: Field(record, "id"),
                    Message: Field(record, "message"),
                    Shape: NullIfEmpty(Field(record, "shape")),
                    Hint: NullIfEmpty(Field(record, "hint")),
                    File: hasLocation ? file : null,
                    Line: hasLocation ? ParseInt(Field(record, "line")) : 0,
                    Column: hasLocation ? ParseInt(Field(record, "column")) : 0
                )
            );
        }
        return events;
    }

    private static List<List<string>> ReadRecords(string csv)
    {
        var records = new List<List<string>>();
        var record = new List<string>();
        var field = new StringBuilder();
        var quoted = false;

        for (var i = 0; i < csv.Length; i++)
        {
            var c = csv[i];
            if (quoted)
            {
                if (c != '"')
                    field.Append(c);
                else if (i + 1 < csv.Length && csv[i + 1] == '"')
                    field.Append(csv[++i]);
                else
                    quoted = false;
            }
            else if (c == '"')
            {
                quoted = true;
            }
            else if (c == ',')
            {
                record.Add(field.ToString());
                field.Clear();
            }
            else if (c is '\n' or '\r')
            {
                if (c == '\r' && i + 1 < csv.Length && csv[i + 1] == '\n')
                    i++;
                EndRecord();
            }
            else
            {
                field.Append(c);
            }
        }
        EndRecord();
        return records;

        void EndRecord()
        {
            record.Add(field.ToString());
            field.Clear();
            if (record.Count > 1 || record[0].Length > 0)
                records.Add(record);
            record = [];
        }
    }

    private static int ParseInt(string value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;
}

/// <summary>A Smithy validation event.</summary>
internal sealed record SmithyValidationEvent(
    string Severity,
    string EventId,
    string Message,
    string? Shape = null,
    string? Hint = null,
    string? File = null,
    int Line = 0,
    int Column = 0
);
