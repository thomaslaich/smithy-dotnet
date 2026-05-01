using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using NSmithy.CodeGeneration.Model;
using NSmithy.Core;
using NSmithy.Core.Traits;

namespace NSmithy.CodeGeneration.CSharp;

#pragma warning disable CA1305 // Source text is assembled from Smithy identifiers and explicitly formatted literals.

public sealed partial class CSharpShapeGenerator
{
    public IReadOnlyList<GeneratedCSharpFile> Generate(
        SmithyModel model,
        CSharpGenerationOptions? options = null
    )
    {
        ArgumentNullException.ThrowIfNull(model);
        options ??= new CSharpGenerationOptions();

        return
        [
            .. model
                .Shapes.Values.Where(shape => ShouldGenerate(shape, options))
                .OrderBy(shape => shape.Id.ToString(), StringComparer.Ordinal)
                .Select(shape => GenerateShape(model, shape, options)),
            .. model
                .Shapes.Values.Where(shape => ShouldGenerateClient(shape, options))
                .OrderBy(shape => shape.Id.ToString(), StringComparer.Ordinal)
                .Select(shape => GenerateClient(model, shape, options)),
            .. model
                .Shapes.Values.Where(shape => ShouldGenerateServer(shape, options))
                .OrderBy(shape => shape.Id.ToString(), StringComparer.Ordinal)
                .Select(shape => GenerateServer(model, shape, options)),
        ];
    }

    private static bool ShouldGenerate(ModelShape shape, CSharpGenerationOptions options)
    {
        return ShouldGenerateNamespace(shape, options)
            && shape.Kind
                is ShapeKind.Structure
                    or ShapeKind.List
                    or ShapeKind.Set
                    or ShapeKind.Map
                    or ShapeKind.Enum
                    or ShapeKind.IntEnum
                    or ShapeKind.Union;
    }

    private static bool ShouldGenerateClient(ModelShape shape, CSharpGenerationOptions options)
    {
        return ShouldGenerateNamespace(shape, options)
            && shape.Kind == ShapeKind.Service
            && (
                shape.Traits.Has(SmithyPrelude.RestJson1Trait)
                || shape.Traits.Has(SmithyPrelude.RestXmlTrait)
                || shape.Traits.Has(SmithyPrelude.SimpleRestJsonTrait)
                || shape.Traits.Has(SmithyPrelude.RpcV2CborTrait)
                || shape.Traits.Has(SmithyPrelude.GrpcTrait)
            );
    }

    private static bool ShouldGenerateServer(ModelShape shape, CSharpGenerationOptions options)
    {
        return ShouldGenerateNamespace(shape, options)
            && shape.Kind == ShapeKind.Service
            && (
                shape.Traits.Has(SmithyPrelude.SimpleRestJsonTrait)
                || shape.Traits.Has(SmithyPrelude.GrpcTrait)
            );
    }

    private static bool ShouldGenerateNamespace(ModelShape shape, CSharpGenerationOptions options)
    {
        return options.GeneratedNamespaces is not { Count: > 0 } generatedNamespaces
            || generatedNamespaces.Contains(shape.Id.Namespace, StringComparer.Ordinal);
    }

    private static GeneratedCSharpFile GenerateShape(
        SmithyModel model,
        ModelShape shape,
        CSharpGenerationOptions options
    )
    {
        var contents = shape.Kind switch
        {
            ShapeKind.Structure when shape.Traits.Has(SmithyPrelude.ErrorTrait) => GenerateError(
                model,
                shape,
                options
            ),
            ShapeKind.Structure => GenerateStructure(model, shape, options),
            ShapeKind.List or ShapeKind.Set => GenerateList(model, shape, options),
            ShapeKind.Map => GenerateMap(model, shape, options),
            ShapeKind.Enum => GenerateStringEnum(shape, options),
            ShapeKind.IntEnum => GenerateIntEnum(shape, options),
            ShapeKind.Union => GenerateUnion(model, shape, options),
            _ => throw new InvalidOperationException(
                $"Shape kind '{shape.Kind}' is not supported by the C# shape generator."
            ),
        };

        return new GeneratedCSharpFile(GetPath(shape), contents);
    }

    // Shared helpers used by both TypeEmitter and ClientEmitter

    private static IEnumerable<MemberShape> GetSortedMembers(
        ModelShape shape,
        MemberShape? excludedMember = null
    )
    {
        return shape
            .Members.Values.Where(member => !ReferenceEquals(member, excludedMember))
            .OrderBy(member => member.Name, StringComparer.Ordinal);
    }

    private static MemberShape? GetErrorMessageMember(ModelShape shape)
    {
        return
            shape.Members.TryGetValue("message", out var member)
            && member.Target.Namespace == SmithyPrelude.Namespace
            && member.Target.Name == "String"
            ? member
            : null;
    }

    private static bool IsSparseTarget(SmithyModel model, ShapeId target)
    {
        return model.Shapes.TryGetValue(target, out var shape)
            && shape.Traits.Has(SmithyPrelude.SparseTrait);
    }

    private static string GetParameter(
        SmithyModel model,
        ModelShape container,
        MemberShape member,
        string currentNamespace,
        CSharpGenerationOptions options
    )
    {
        var parameterType = GetMemberParameterType(
            model,
            container,
            member,
            currentNamespace,
            options
        );
        var parameterName = CSharpIdentifier.ParameterName(member.Name);
        var defaultValue =
            IsNullableMember(container, member, options)
            || GetEffectiveDefaultValue(container, member, options) is not null
                ? " = null"
                : string.Empty;
        return $"{parameterType} {parameterName}{defaultValue}";
    }

    private static string GetAssignment(
        SmithyModel model,
        ModelShape container,
        MemberShape member,
        string parameterName,
        string currentNamespace,
        CSharpGenerationOptions options
    )
    {
        var effectiveDefault = GetEffectiveDefaultValue(container, member, options);
        if (effectiveDefault is not null)
        {
            return $"{parameterName} ?? {GetDefaultExpression(model, member.Target, effectiveDefault.Value, currentNamespace, options.BaseNamespace)}";
        }

        if (!IsNullableMember(container, member, options) && IsReferenceType(model, member.Target))
        {
            return $"{parameterName} ?? throw new ArgumentNullException(nameof({parameterName}))";
        }

        return parameterName;
    }

    private static string GetMemberType(
        SmithyModel model,
        ModelShape container,
        MemberShape member,
        string currentNamespace,
        CSharpGenerationOptions options
    )
    {
        return GetValueType(
            model,
            member.Target,
            nullable: IsNullableMember(container, member, options),
            currentNamespace,
            options.BaseNamespace
        );
    }

    private static string GetMemberParameterType(
        SmithyModel model,
        ModelShape container,
        MemberShape member,
        string currentNamespace,
        CSharpGenerationOptions options
    )
    {
        return GetValueType(
            model,
            member.Target,
            nullable: IsNullableMember(container, member, options)
                || GetEffectiveDefaultValue(container, member, options) is not null,
            currentNamespace,
            options.BaseNamespace
        );
    }

    private static bool IsNullableMember(
        ModelShape container,
        MemberShape member,
        CSharpGenerationOptions options
    )
    {
        return !member.IsRequired && GetEffectiveDefaultValue(container, member, options) is null;
    }

    private static Document? GetEffectiveDefaultValue(
        ModelShape container,
        MemberShape member,
        CSharpGenerationOptions options
    )
    {
        if (member.DefaultValue is not { Kind: not DocumentKind.Null } value)
        {
            return null;
        }

        return value;
    }

    private static string GetValueType(
        SmithyModel model,
        ShapeId target,
        bool nullable,
        string currentNamespace,
        string? baseNamespace
    )
    {
        var type = GetNonNullableValueType(model, target, currentNamespace, baseNamespace);
        return nullable ? $"{type}?" : type;
    }

    private static string GetNonNullableValueType(
        SmithyModel model,
        ShapeId target,
        string currentNamespace,
        string? baseNamespace
    )
    {
        if (target.Namespace == SmithyPrelude.Namespace)
        {
            return target.Name switch
            {
                "Blob" => "byte[]",
                "Boolean" => "bool",
                "Byte" => "sbyte",
                "Short" => "short",
                "Integer" => "int",
                "Long" => "long",
                "Float" => "float",
                "Double" => "double",
                "BigInteger" => "System.Numerics.BigInteger",
                "BigDecimal" => "decimal",
                "Timestamp" => "DateTimeOffset",
                "String" => "string",
                "Document" => "Document",
                _ => CSharpIdentifier.TypeName(target.Name),
            };
        }

        return GetTypeReference(target, currentNamespace, baseNamespace);
    }

    private static bool IsReferenceType(SmithyModel model, ShapeId target)
    {
        if (target.Namespace == SmithyPrelude.Namespace)
        {
            return target.Name is "Blob" or "String" or "Document";
        }

        return model.GetShape(target).Kind
            is ShapeKind.Structure
                or ShapeKind.List
                or ShapeKind.Set
                or ShapeKind.Map
                or ShapeKind.Union;
    }

    private static string GetDefaultExpression(
        SmithyModel model,
        ShapeId target,
        Document value,
        string currentNamespace,
        string? baseNamespace
    )
    {
        if (target.Namespace == SmithyPrelude.Namespace)
        {
            return target.Name switch
            {
                "Boolean" => value.AsBoolean() ? "true" : "false",
                "Byte" or "Short" or "Integer" or "Long" => value
                    .AsNumber()
                    .ToString(CultureInfo.InvariantCulture),
                "Float" => string.Create(
                    CultureInfo.InvariantCulture,
                    $"{value.AsNumber().ToString(CultureInfo.InvariantCulture)}f"
                ),
                "Double" => string.Create(
                    CultureInfo.InvariantCulture,
                    $"{value.AsNumber().ToString(CultureInfo.InvariantCulture)}d"
                ),
                "BigDecimal" => string.Create(
                    CultureInfo.InvariantCulture,
                    $"{value.AsNumber().ToString(CultureInfo.InvariantCulture)}m"
                ),
                "String" => FormatString(value.AsString()),
                "Document" => "Document.Null",
                _ => throw new SmithyException(
                    $"Default values for target '{target}' are not supported yet."
                ),
            };
        }

        var targetShape = model.GetShape(target);
        return targetShape.Kind switch
        {
            ShapeKind.Enum =>
                $"new {GetTypeReference(target, currentNamespace, baseNamespace)}({FormatString(value.AsString())})",
            ShapeKind.IntEnum =>
                $"({GetTypeReference(target, currentNamespace, baseNamespace)}){(int)value.AsNumber()}",
            _ => throw new SmithyException(
                $"Default values for target '{target}' are not supported yet."
            ),
        };
    }

    private static CSharpWriter CreateFileBuilder(
        ModelShape shape,
        CSharpGenerationOptions options,
        IReadOnlyList<string>? extraUsings = null
    )
    {
        var builder = new CSharpWriter();
        builder.Line("// <auto-generated />");
        builder.Line("#nullable enable");
        builder.Line();
        builder.Line("using System;");
        builder.Line("using System.Collections.Generic;");
        builder.Line("using System.Linq;");
        builder.Line("using NSmithy.Core;");
        builder.Line("using NSmithy.Core.Annotations;");
        foreach (var @namespace in extraUsings ?? [])
        {
            builder.Line($"using {@namespace};");
        }

        builder.Line();
        builder.Line(
            $"namespace {CSharpIdentifier.Namespace(shape.Id.Namespace, options.BaseNamespace)};"
        );
        builder.Line();
        return builder;
    }

    private static string GetPath(ModelShape shape)
    {
        var namespacePath = string.Join(
            '/',
            shape.Id.Namespace.Split('.').Select(CSharpIdentifier.FileSegment)
        );
        return $"{namespacePath}/{GetTypeName(shape.Id)}.g.cs";
    }

    private static string GetClientPath(ModelShape shape)
    {
        var namespacePath = string.Join(
            '/',
            shape.Id.Namespace.Split('.').Select(CSharpIdentifier.FileSegment)
        );
        return $"{namespacePath}/{GetTypeName(shape.Id)}Client.g.cs";
    }

    private static string GetServerPath(ModelShape shape)
    {
        var namespacePath = string.Join(
            '/',
            shape.Id.Namespace.Split('.').Select(CSharpIdentifier.FileSegment)
        );
        return $"{namespacePath}/{GetTypeName(shape.Id)}Server.g.cs";
    }

    private static string GetTypeName(ShapeId id)
    {
        return CSharpIdentifier.TypeName(id.Name);
    }

    private static string GetTypeReference(
        ShapeId id,
        string currentNamespace,
        string? baseNamespace
    )
    {
        var typeName = GetTypeName(id);
        return string.Equals(id.Namespace, currentNamespace, StringComparison.Ordinal)
            ? typeName
            : $"global::{CSharpIdentifier.Namespace(id.Namespace, baseNamespace)}.{typeName}";
    }

    private static string FormatString(string value)
    {
        var builder = new StringBuilder(value.Length + 2);
        builder.Append('"');
        foreach (var character in value)
        {
            builder.Append(
                character switch
                {
                    '\\' => "\\\\",
                    '"' => "\\\"",
                    '\0' => "\\0",
                    '\a' => "\\a",
                    '\b' => "\\b",
                    '\f' => "\\f",
                    '\n' => "\\n",
                    '\r' => "\\r",
                    '\t' => "\\t",
                    '\v' => "\\v",
                    _ => character.ToString(),
                }
            );
        }

        builder.Append('"');
        return builder.ToString();
    }

    private sealed record HttpBinding(string Method, string Uri);
}

#pragma warning restore CA1305
