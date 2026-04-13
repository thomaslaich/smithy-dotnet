using Smithy.NET.Core;

namespace Smithy.NET.CodeGeneration.Model;

public static class SmithyPrelude
{
    public const string Namespace = "smithy.api";

    public static ShapeId RequiredTrait { get; } = new(Namespace, "required");

    public static ShapeId DefaultTrait { get; } = new(Namespace, "default");

    public static ShapeId ClientOptionalTrait { get; } = new(Namespace, "clientOptional");

    public static ShapeId ErrorTrait { get; } = new(Namespace, "error");
}
