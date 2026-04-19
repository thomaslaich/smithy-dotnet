using Smithy.NET.Core;

namespace Smithy.NET.CodeGeneration.Model;

public static class SmithyPrelude
{
    public const string Namespace = "smithy.api";

    public static ShapeId RequiredTrait { get; } = new(Namespace, "required");

    public static ShapeId DefaultTrait { get; } = new(Namespace, "default");

    public static ShapeId ClientOptionalTrait { get; } = new(Namespace, "clientOptional");

    public static ShapeId InputTrait { get; } = new(Namespace, "input");

    public static ShapeId OutputTrait { get; } = new(Namespace, "output");

    public static ShapeId ErrorTrait { get; } = new(Namespace, "error");

    public static ShapeId EnumValueTrait { get; } = new(Namespace, "enumValue");

    public static ShapeId JsonNameTrait { get; } = new(Namespace, "jsonName");

    public static ShapeId SparseTrait { get; } = new(Namespace, "sparse");
}
