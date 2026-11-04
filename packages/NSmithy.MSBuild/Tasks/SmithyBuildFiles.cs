using System.Text.Json;
using System.Text.Json.Nodes;

namespace NSmithy.Contracts;

/// <summary>Reading smithy-build.json files the way the Smithy CLI does.</summary>
internal static class SmithyBuildFiles
{
    private static readonly JsonDocumentOptions ReadOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static JsonObject Parse(string path) =>
        JsonNode.Parse(File.ReadAllText(path), documentOptions: ReadOptions) as JsonObject
        ?? throw new JsonException($"{path} is not a JSON object.");

    /// <summary>
    /// The model files and directories a build file names itself: its top-level <c>sources</c>
    /// and <c>imports</c>, as written. These are a project's own model files.
    /// </summary>
    public static List<string> OwnSources(JsonObject root)
    {
        var sources = new List<string>();
        foreach (var key in new[] { "sources", "imports" })
            if (root[key] is JsonArray paths)
                foreach (var path in paths)
                    if (path?.GetValue<string>() is { } value)
                        sources.Add(value);
        return sources;
    }
}
