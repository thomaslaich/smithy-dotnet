using System.Globalization;
using System.Text;
using Smithy.NET.CodeGeneration.Model;
using Smithy.NET.Core;

namespace Smithy.NET.CodeGeneration.CSharp;

#pragma warning disable CA1305 // Source text is assembled from Smithy identifiers and explicitly formatted literals.

public sealed class CSharpShapeGenerator
{
    public IReadOnlyList<GeneratedCSharpFile> Generate(
        SmithyModel model,
        CSharpGenerationOptions? options = null
    )
    {
        ArgumentNullException.ThrowIfNull(model);
        options ??= new CSharpGenerationOptions();

        return model
            .Shapes.Values.Where(ShouldGenerate)
            .OrderBy(shape => shape.Id.ToString(), StringComparer.Ordinal)
            .Select(shape => GenerateShape(model, shape, options))
            .ToArray();
    }

    private static bool ShouldGenerate(ModelShape shape)
    {
        return shape.Kind
            is ShapeKind.Structure
                or ShapeKind.List
                or ShapeKind.Set
                or ShapeKind.Map
                or ShapeKind.Enum
                or ShapeKind.IntEnum
                or ShapeKind.Union;
    }

    private static GeneratedCSharpFile GenerateShape(
        SmithyModel model,
        ModelShape shape,
        CSharpGenerationOptions options
    )
    {
        var contents = shape.Kind switch
        {
            ShapeKind.Structure when shape.Traits.Has(SmithyPrelude.ErrorTrait) =>
                GenerateError(model, shape, options),
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

    private static string GenerateStructure(
        SmithyModel model,
        ModelShape shape,
        CSharpGenerationOptions options
    )
    {
        var builder = CreateFileBuilder(shape, options);
        var typeName = GetTypeName(shape.Id);
        builder.AppendLine($"public sealed partial record class {typeName}");
        builder.AppendLine("{");
        AppendConstructor(builder, model, shape, typeName, options, baseCall: null);
        AppendProperties(builder, model, shape, options);
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static string GenerateError(
        SmithyModel model,
        ModelShape shape,
        CSharpGenerationOptions options
    )
    {
        var builder = CreateFileBuilder(shape, options);
        var typeName = GetTypeName(shape.Id);
        builder.AppendLine($"public sealed partial class {typeName} : Exception");
        builder.AppendLine("{");
        AppendConstructor(builder, model, shape, typeName, options, "base(message)");
        AppendProperties(builder, model, shape, options);
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static string GenerateList(
        SmithyModel model,
        ModelShape shape,
        CSharpGenerationOptions options
    )
    {
        var builder = CreateFileBuilder(shape, options);
        var typeName = GetTypeName(shape.Id);
        var member = shape.Members.TryGetValue("member", out var value)
            ? value
            : throw new SmithyException($"List shape '{shape.Id}' is missing its member target.");
        var memberType = GetValueType(
            model,
            member.Target,
            nullable: shape.Traits.Has(SmithyPrelude.SparseTrait),
            currentNamespace: shape.Id.Namespace,
            baseNamespace: options.BaseNamespace
        );
        builder.AppendLine($"public sealed partial record class {typeName}");
        builder.AppendLine("{");
        builder.AppendLine($"    public {typeName}(IEnumerable<{memberType}> values)");
        builder.AppendLine("    {");
        builder.AppendLine("        ArgumentNullException.ThrowIfNull(values);");
        builder.AppendLine("        Values = Array.AsReadOnly(values.ToArray());");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine($"    public IReadOnlyList<{memberType}> Values {{ get; }}");
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static string GenerateMap(
        SmithyModel model,
        ModelShape shape,
        CSharpGenerationOptions options
    )
    {
        var builder = CreateFileBuilder(shape, options);
        var typeName = GetTypeName(shape.Id);
        var key = shape.Members.TryGetValue("key", out var keyValue)
            ? keyValue
            : throw new SmithyException($"Map shape '{shape.Id}' is missing its key target.");
        var value = shape.Members.TryGetValue("value", out var mapValue)
            ? mapValue
            : throw new SmithyException($"Map shape '{shape.Id}' is missing its value target.");
        var keyType = GetValueType(
            model,
            key.Target,
            nullable: false,
            currentNamespace: shape.Id.Namespace,
            baseNamespace: options.BaseNamespace
        );
        var valueType = GetValueType(
            model,
            value.Target,
            nullable: shape.Traits.Has(SmithyPrelude.SparseTrait),
            currentNamespace: shape.Id.Namespace,
            baseNamespace: options.BaseNamespace
        );

        builder.AppendLine($"public sealed partial record class {typeName}");
        builder.AppendLine("{");
        builder.AppendLine(
            $"    public {typeName}(IReadOnlyDictionary<{keyType}, {valueType}> values)"
        );
        builder.AppendLine("    {");
        builder.AppendLine("        ArgumentNullException.ThrowIfNull(values);");
        builder.AppendLine($"        Values = new Dictionary<{keyType}, {valueType}>(values);");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine($"    public IReadOnlyDictionary<{keyType}, {valueType}> Values {{ get; }}");
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static string GenerateStringEnum(ModelShape shape, CSharpGenerationOptions options)
    {
        var builder = CreateFileBuilder(shape, options);
        var typeName = GetTypeName(shape.Id);
        builder.AppendLine($"public readonly partial record struct {typeName}(string Value)");
        builder.AppendLine("{");
        foreach (var member in shape.Members.Values.OrderBy(member => member.Name, StringComparer.Ordinal))
        {
            var propertyName = CSharpIdentifier.PropertyName(member.Name);
            var value = member.Traits.GetValueOrDefault(SmithyPrelude.EnumValueTrait)?.AsString() ?? member.Name;
            builder.AppendLine(
                $"    public static {typeName} {propertyName} {{ get; }} = new({FormatString(value)});"
            );
        }

        builder.AppendLine();
        builder.AppendLine("    public override string ToString()");
        builder.AppendLine("    {");
        builder.AppendLine("        return Value;");
        builder.AppendLine("    }");
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static string GenerateIntEnum(ModelShape shape, CSharpGenerationOptions options)
    {
        var builder = CreateFileBuilder(shape, options);
        var typeName = GetTypeName(shape.Id);
        builder.AppendLine($"public enum {typeName}");
        builder.AppendLine("{");
        foreach (var member in shape.Members.Values.OrderBy(member => member.Name, StringComparer.Ordinal))
        {
            var propertyName = CSharpIdentifier.PropertyName(member.Name);
            var value = member.Traits.GetValueOrDefault(SmithyPrelude.EnumValueTrait)?.AsNumber();
            var suffix = value is null
                ? string.Empty
                : string.Create(CultureInfo.InvariantCulture, $" = {(int)value.Value}");
            builder.AppendLine($"    {propertyName}{suffix},");
        }

        builder.AppendLine("}");
        return builder.ToString();
    }

    private static string GenerateUnion(
        SmithyModel model,
        ModelShape shape,
        CSharpGenerationOptions options
    )
    {
        var builder = CreateFileBuilder(shape, options);
        var typeName = GetTypeName(shape.Id);
        builder.AppendLine($"public abstract partial record class {typeName}");
        builder.AppendLine("{");
        builder.AppendLine($"    private protected {typeName}() {{ }}");
        builder.AppendLine();
        foreach (var member in shape.Members.Values.OrderBy(member => member.Name, StringComparer.Ordinal))
        {
            var variantName = CSharpIdentifier.TypeName(member.Name);
            var valueType = GetValueType(
                model,
                member.Target,
                nullable: false,
                currentNamespace: shape.Id.Namespace,
                baseNamespace: options.BaseNamespace
            );
            builder.AppendLine(
                $"    public sealed partial record class {variantName}({valueType} Value) : {typeName};"
            );
        }

        builder.AppendLine();
        builder.AppendLine(
            $"    public sealed partial record class Unknown(string Tag, Document Value) : {typeName};"
        );
        builder.AppendLine();
        builder.AppendLine("    public T Match<T>(");
        foreach (var member in shape.Members.Values.OrderBy(member => member.Name, StringComparer.Ordinal))
        {
            var variantName = CSharpIdentifier.TypeName(member.Name);
            var parameterName = CSharpIdentifier.ParameterName(member.Name);
            builder.AppendLine($"        Func<{variantName}, T> {parameterName},");
        }

        builder.AppendLine("        Func<Unknown, T> unknown)");
        builder.AppendLine("    {");
        builder.AppendLine("        return this switch");
        builder.AppendLine("        {");
        foreach (var member in shape.Members.Values.OrderBy(member => member.Name, StringComparer.Ordinal))
        {
            var variantName = CSharpIdentifier.TypeName(member.Name);
            var parameterName = CSharpIdentifier.ParameterName(member.Name);
            builder.AppendLine($"            {variantName} value => {parameterName}(value),");
        }

        builder.AppendLine("            Unknown value => unknown(value),");
        builder.AppendLine("            _ => throw new InvalidOperationException(\"Unknown union variant.\"),");
        builder.AppendLine("        };");
        builder.AppendLine("    }");
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static void AppendConstructor(
        StringBuilder builder,
        SmithyModel model,
        ModelShape shape,
        string typeName,
        CSharpGenerationOptions options,
        string? baseCall
    )
    {
        var members = shape.Members.Values.OrderBy(member => member.Name, StringComparer.Ordinal).ToArray();
        builder.Append($"    public {typeName}(");
        if (baseCall is not null)
        {
            builder.Append("string? message = null");
            if (members.Length > 0)
            {
                builder.Append(", ");
            }
        }

        builder.Append(
            string.Join(
                ", ",
                members.Select(member =>
                    GetParameter(model, member, shape.Id.Namespace, options.BaseNamespace)
                )
            )
        );
        builder.AppendLine(")");
        if (baseCall is not null)
        {
            builder.AppendLine($"        : {baseCall}");
        }

        builder.AppendLine("    {");
        foreach (var member in members)
        {
            var propertyName = CSharpIdentifier.PropertyName(member.Name);
            var parameterName = CSharpIdentifier.ParameterName(member.Name);
            builder.AppendLine(
                $"        {propertyName} = {GetAssignment(model, member, parameterName, shape.Id.Namespace, options.BaseNamespace)};"
            );
        }

        builder.AppendLine("    }");
        if (members.Length > 0)
        {
            builder.AppendLine();
        }
    }

    private static void AppendProperties(
        StringBuilder builder,
        SmithyModel model,
        ModelShape shape,
        CSharpGenerationOptions options
    )
    {
        foreach (var member in shape.Members.Values.OrderBy(member => member.Name, StringComparer.Ordinal))
        {
            var propertyName = CSharpIdentifier.PropertyName(member.Name);
            var propertyType = GetMemberType(
                model,
                member,
                shape.Id.Namespace,
                options.BaseNamespace
            );
            builder.AppendLine($"    public {propertyType} {propertyName} {{ get; }}");
        }
    }

    private static string GetParameter(
        SmithyModel model,
        MemberShape member,
        string currentNamespace,
        string? baseNamespace
    )
    {
        var parameterType = GetMemberParameterType(
            model,
            member,
            currentNamespace,
            baseNamespace
        );
        var parameterName = CSharpIdentifier.ParameterName(member.Name);
        var defaultValue = IsOptional(member) ? " = null" : string.Empty;
        return $"{parameterType} {parameterName}{defaultValue}";
    }

    private static string GetAssignment(
        SmithyModel model,
        MemberShape member,
        string parameterName,
        string currentNamespace,
        string? baseNamespace
    )
    {
        if (member.DefaultValue is not null)
        {
            return $"{parameterName} ?? {GetDefaultExpression(model, member.Target, member.DefaultValue.Value, currentNamespace, baseNamespace)}";
        }

        if (!IsOptional(member) && IsReferenceType(model, member.Target))
        {
            return $"{parameterName} ?? throw new ArgumentNullException(nameof({parameterName}))";
        }

        return parameterName;
    }

    private static string GetMemberType(
        SmithyModel model,
        MemberShape member,
        string currentNamespace,
        string? baseNamespace
    )
    {
        return GetValueType(
            model,
            member.Target,
            nullable: IsOptional(member) && member.DefaultValue is null,
            currentNamespace,
            baseNamespace
        );
    }

    private static string GetMemberParameterType(
        SmithyModel model,
        MemberShape member,
        string currentNamespace,
        string? baseNamespace
    )
    {
        return GetValueType(
            model,
            member.Target,
            nullable: IsOptional(member) || member.DefaultValue is not null,
            currentNamespace,
            baseNamespace
        );
    }

    private static bool IsOptional(MemberShape member)
    {
        return !member.IsRequired || member.IsClientOptional;
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
            is ShapeKind.Structure or ShapeKind.List or ShapeKind.Set or ShapeKind.Map or ShapeKind.Union;
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

    private static StringBuilder CreateFileBuilder(
        ModelShape shape,
        CSharpGenerationOptions options
    )
    {
        var builder = new StringBuilder();
        builder.AppendLine("// <auto-generated />");
        builder.AppendLine("#nullable enable");
        builder.AppendLine();
        builder.AppendLine("using System;");
        builder.AppendLine("using System.Collections.Generic;");
        builder.AppendLine("using System.Linq;");
        builder.AppendLine("using Smithy.NET.Core;");
        builder.AppendLine();
        builder.AppendLine(
            $"namespace {CSharpIdentifier.Namespace(shape.Id.Namespace, options.BaseNamespace)};"
        );
        builder.AppendLine();
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

    private static string GetTypeName(ShapeId id)
    {
        return CSharpIdentifier.TypeName(id.Name);
    }

    private static string GetTypeReference(ShapeId id, string currentNamespace, string? baseNamespace)
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
}

#pragma warning restore CA1305
