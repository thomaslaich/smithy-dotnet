using System.Collections.ObjectModel;
using System.Text.Json;
using Smithy.NET.CodeGeneration.Model;
using Smithy.NET.Core;
using Smithy.NET.Core.Traits;

namespace Smithy.NET.CodeGeneration;

public static class SmithyJsonAstReader
{
    public static SmithyModel Read(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        using var document = JsonDocument.Parse(
            json,
            new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            }
        );

        return Read(document.RootElement);
    }

    public static async ValueTask<SmithyModel> ReadAsync(
        Stream stream,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var document = await JsonDocument
            .ParseAsync(
                stream,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip,
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        return Read(document.RootElement);
    }

    public static SmithyModel Read(JsonElement root)
    {
        var modelRoot = UnwrapBuildOutput(root);
        var smithyVersion = modelRoot.TryGetProperty("smithy", out var smithyProperty)
            ? smithyProperty.GetString() ?? string.Empty
            : throw new SmithyException(
                "Smithy JSON AST is missing the required 'smithy' version property."
            );

        var metadata = modelRoot.TryGetProperty("metadata", out var metadataProperty)
            ? ReadMetadata(metadataProperty)
            : new ReadOnlyDictionary<string, Document>(new Dictionary<string, Document>());

        if (
            !modelRoot.TryGetProperty("shapes", out var shapesProperty)
            || shapesProperty.ValueKind != JsonValueKind.Object
        )
        {
            throw new SmithyException("Smithy JSON AST is missing the required 'shapes' object.");
        }

        var shapes = new Dictionary<ShapeId, ModelShape>();
        foreach (var shapeProperty in shapesProperty.EnumerateObject())
        {
            var id = ShapeId.Parse(shapeProperty.Name);
            shapes.Add(id, ReadShape(id, shapeProperty.Value));
        }

        return new SmithyModel(
            smithyVersion,
            metadata,
            new ReadOnlyDictionary<ShapeId, ModelShape>(shapes)
        );
    }

    private static JsonElement UnwrapBuildOutput(JsonElement root)
    {
        if (root.TryGetProperty("smithy", out _) && root.TryGetProperty("shapes", out _))
        {
            return root;
        }

        if (root.TryGetProperty("model", out var model) && model.ValueKind == JsonValueKind.Object)
        {
            return model;
        }

        throw new SmithyException(
            "Input is not a Smithy JSON AST or a supported Smithy build output document."
        );
    }

    private static IReadOnlyDictionary<string, Document> ReadMetadata(JsonElement metadata)
    {
        if (metadata.ValueKind != JsonValueKind.Object)
        {
            throw new SmithyException("Smithy JSON AST 'metadata' must be an object.");
        }

        return new ReadOnlyDictionary<string, Document>(
            metadata
                .EnumerateObject()
                .ToDictionary(
                    property => property.Name,
                    property => Document.FromJsonElement(property.Value),
                    StringComparer.Ordinal
                )
        );
    }

    private static ModelShape ReadShape(ShapeId id, JsonElement shape)
    {
        if (shape.ValueKind != JsonValueKind.Object)
        {
            throw new SmithyException($"Shape '{id}' must be an object.");
        }

        var kind = shape.TryGetProperty("type", out var typeProperty)
            ? ReadShapeKind(typeProperty.GetString())
            : ShapeKind.Unknown;
        var traits = shape.TryGetProperty("traits", out var traitsProperty)
            ? ReadTraits(traitsProperty)
            : TraitCollection.None;
        var members = shape.TryGetProperty("members", out var membersProperty)
            ? ReadMembers(id, kind, membersProperty)
            : ReadCollectionMembers(id, kind, shape);

        return new ModelShape(
            id,
            kind,
            traits,
            members,
            ReadOptionalShapeId(shape, "target"),
            ReadOptionalShapeId(shape, "input"),
            ReadOptionalShapeId(shape, "output"),
            ReadShapeIdList(shape, "errors"),
            ReadShapeIdList(shape, "operations"),
            ReadShapeIdList(shape, "resources")
        );
    }

    private static TraitCollection ReadTraits(JsonElement traits)
    {
        if (traits.ValueKind != JsonValueKind.Object)
        {
            throw new SmithyException("Smithy JSON AST 'traits' must be an object.");
        }

        return new TraitCollection(
            traits
                .EnumerateObject()
                .ToDictionary(
                    property => ShapeId.Parse(property.Name),
                    property => Document.FromJsonElement(property.Value)
                )
        );
    }

    private static IReadOnlyDictionary<string, MemberShape> ReadMembers(
        ShapeId containerId,
        ShapeKind containerKind,
        JsonElement members
    )
    {
        if (members.ValueKind != JsonValueKind.Object)
        {
            throw new SmithyException(
                $"Shape '{containerId}' has a 'members' property that is not an object."
            );
        }

        var result = new Dictionary<string, MemberShape>(StringComparer.Ordinal);
        foreach (var memberProperty in members.EnumerateObject())
        {
            result.Add(
                memberProperty.Name,
                ReadMember(containerId, containerKind, memberProperty.Name, memberProperty.Value)
            );
        }

        return new ReadOnlyDictionary<string, MemberShape>(result);
    }

    private static IReadOnlyDictionary<string, MemberShape> ReadCollectionMembers(
        ShapeId containerId,
        ShapeKind containerKind,
        JsonElement shape
    )
    {
        return containerKind switch
        {
            ShapeKind.List or ShapeKind.Set when shape.TryGetProperty("member", out var member) =>
                ReadSyntheticMembers(containerId, containerKind, [new("member", member)]),
            ShapeKind.Map
                when shape.TryGetProperty("key", out var key)
                    && shape.TryGetProperty("value", out var value) => ReadSyntheticMembers(
                containerId,
                containerKind,
                [new("key", key), new("value", value)]
            ),
            _ => new ReadOnlyDictionary<string, MemberShape>(new Dictionary<string, MemberShape>()),
        };
    }

    private static IReadOnlyDictionary<string, MemberShape> ReadSyntheticMembers(
        ShapeId containerId,
        ShapeKind containerKind,
        IReadOnlyList<KeyValuePair<string, JsonElement>> members
    )
    {
        return new ReadOnlyDictionary<string, MemberShape>(
            members.ToDictionary(
                member => member.Key,
                member => ReadMember(containerId, containerKind, member.Key, member.Value),
                StringComparer.Ordinal
            )
        );
    }

    private static MemberShape ReadMember(
        ShapeId containerId,
        ShapeKind containerKind,
        string memberName,
        JsonElement member
    )
    {
        if (
            !member.TryGetProperty("target", out var targetProperty)
            && containerKind is not (ShapeKind.Enum or ShapeKind.IntEnum)
        )
        {
            throw new SmithyException($"Member '{containerId}${memberName}' is missing a target.");
        }

        var traits = member.TryGetProperty("traits", out var traitsProperty)
            ? ReadTraits(traitsProperty)
            : TraitCollection.None;
        var defaultValue = traits.GetValueOrDefault(SmithyPrelude.DefaultTrait);

        return new MemberShape(
            containerId.WithMember(memberName),
            memberName,
            targetProperty.ValueKind == JsonValueKind.String
                ? ShapeId.Parse(targetProperty.GetString() ?? string.Empty)
                : GetSyntheticEnumMemberTarget(containerKind),
            traits,
            defaultValue
        );
    }

    private static ShapeId? ReadOptionalShapeId(JsonElement shape, string propertyName)
    {
        return shape.TryGetProperty(propertyName, out var property) ? ReadShapeId(property) : null;
    }

    private static IReadOnlyList<ShapeId> ReadShapeIdList(JsonElement shape, string propertyName)
    {
        if (!shape.TryGetProperty(propertyName, out var property))
        {
            return Array.Empty<ShapeId>();
        }

        if (property.ValueKind != JsonValueKind.Array)
        {
            throw new SmithyException(
                $"Shape property '{propertyName}' must be an array of shape IDs."
            );
        }

        return property.EnumerateArray().Select(ReadShapeId).ToArray();
    }

    private static ShapeId ReadShapeId(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            return ShapeId.Parse(value.GetString() ?? string.Empty);
        }

        if (
            value.ValueKind == JsonValueKind.Object
            && value.TryGetProperty("target", out var target)
            && target.ValueKind == JsonValueKind.String
        )
        {
            return ShapeId.Parse(target.GetString() ?? string.Empty);
        }

        throw new SmithyException(
            "Expected a shape ID string or an object with a string 'target' property."
        );
    }

    private static ShapeKind ReadShapeKind(string? value)
    {
        return value switch
        {
            "blob" => ShapeKind.Blob,
            "boolean" => ShapeKind.Boolean,
            "byte" => ShapeKind.Byte,
            "short" => ShapeKind.Short,
            "integer" => ShapeKind.Integer,
            "long" => ShapeKind.Long,
            "float" => ShapeKind.Float,
            "double" => ShapeKind.Double,
            "bigInteger" => ShapeKind.BigInteger,
            "bigDecimal" => ShapeKind.BigDecimal,
            "timestamp" => ShapeKind.Timestamp,
            "string" => ShapeKind.String,
            "enum" => ShapeKind.Enum,
            "intEnum" => ShapeKind.IntEnum,
            "document" => ShapeKind.Document,
            "list" => ShapeKind.List,
            "set" => ShapeKind.Set,
            "map" => ShapeKind.Map,
            "structure" => ShapeKind.Structure,
            "union" => ShapeKind.Union,
            "service" => ShapeKind.Service,
            "operation" => ShapeKind.Operation,
            "resource" => ShapeKind.Resource,
            "member" => ShapeKind.Member,
            "apply" => ShapeKind.Apply,
            _ => ShapeKind.Unknown,
        };
    }

    private static ShapeId GetSyntheticEnumMemberTarget(ShapeKind containerKind)
    {
        return containerKind switch
        {
            ShapeKind.Enum => new ShapeId(SmithyPrelude.Namespace, "String"),
            ShapeKind.IntEnum => new ShapeId(SmithyPrelude.Namespace, "Integer"),
            _ => throw new InvalidOperationException(
                $"Cannot synthesize an enum member target for '{containerKind}'."
            ),
        };
    }
}
