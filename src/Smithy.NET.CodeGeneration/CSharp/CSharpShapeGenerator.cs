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

        return
        [
            .. model
                .Shapes.Values.Where(ShouldGenerate)
                .OrderBy(shape => shape.Id.ToString(), StringComparer.Ordinal)
                .Select(shape => GenerateShape(model, shape, options)),
        ];
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

    private static string GenerateStructure(
        SmithyModel model,
        ModelShape shape,
        CSharpGenerationOptions options
    )
    {
        var builder = CreateFileBuilder(shape, options);
        var typeName = GetTypeName(shape.Id);
        builder.Line($"public sealed partial record class {typeName}");
        builder.Block(() =>
        {
            AppendConstructor(builder, model, shape, typeName, options, baseCall: null);
            AppendProperties(builder, model, shape, options);
        });
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
        builder.Line($"public sealed partial class {typeName} : Exception");
        builder.Block(() =>
        {
            var messageMember = GetErrorMessageMember(shape);
            AppendErrorConstructor(builder, model, shape, typeName, options, messageMember);
            AppendProperties(builder, model, shape, options, messageMember);
        });
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
        builder.Line($"public sealed partial record class {typeName}");
        builder.Block(() =>
        {
            builder.Line($"public {typeName}(IEnumerable<{memberType}> values)");
            builder.Block(() =>
            {
                builder.Line("ArgumentNullException.ThrowIfNull(values);");
                builder.Line("Values = Array.AsReadOnly(values.ToArray());");
            });
            builder.Line();
            builder.Line($"public IReadOnlyList<{memberType}> Values {{ get; }}");
        });
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

        builder.Line($"public sealed partial record class {typeName}");
        builder.Block(() =>
        {
            builder.Line($"public {typeName}(IReadOnlyDictionary<{keyType}, {valueType}> values)");
            builder.Block(() =>
            {
                builder.Line("ArgumentNullException.ThrowIfNull(values);");
                builder.Line(
                    $"Values = new System.Collections.ObjectModel.ReadOnlyDictionary<{keyType}, {valueType}>(new Dictionary<{keyType}, {valueType}>(values));"
                );
            });
            builder.Line();
            builder.Line($"public IReadOnlyDictionary<{keyType}, {valueType}> Values {{ get; }}");
        });
        return builder.ToString();
    }

    private static string GenerateStringEnum(ModelShape shape, CSharpGenerationOptions options)
    {
        var builder = CreateFileBuilder(shape, options);
        var typeName = GetTypeName(shape.Id);
        builder.Line($"public readonly partial record struct {typeName}(string Value)");
        builder.Block(() =>
        {
            foreach (
                var member in shape.Members.Values.OrderBy(
                    member => member.Name,
                    StringComparer.Ordinal
                )
            )
            {
                var propertyName = CSharpIdentifier.PropertyName(member.Name);
                var value =
                    member.Traits.GetValueOrDefault(SmithyPrelude.EnumValueTrait)?.AsString()
                    ?? member.Name;
                builder.Line(
                    $"public static {typeName} {propertyName} {{ get; }} = new({FormatString(value)});"
                );
            }

            builder.Line();
            builder.Line("public override string ToString()");
            builder.Block(() =>
            {
                builder.Line("return Value;");
            });
        });
        return builder.ToString();
    }

    private static string GenerateIntEnum(ModelShape shape, CSharpGenerationOptions options)
    {
        var builder = CreateFileBuilder(shape, options);
        var typeName = GetTypeName(shape.Id);
        builder.Line($"public enum {typeName}");
        builder.Block(() =>
        {
            foreach (
                var member in shape.Members.Values.OrderBy(
                    member => member.Name,
                    StringComparer.Ordinal
                )
            )
            {
                var propertyName = CSharpIdentifier.PropertyName(member.Name);
                var value = member
                    .Traits.GetValueOrDefault(SmithyPrelude.EnumValueTrait)
                    ?.AsNumber();
                var suffix = value is null
                    ? string.Empty
                    : string.Create(CultureInfo.InvariantCulture, $" = {(int)value.Value}");
                builder.Line($"{propertyName}{suffix},");
            }
        });
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
        builder.Line($"public abstract partial record class {typeName}");
        var members = shape
            .Members.Values.OrderBy(member => member.Name, StringComparer.Ordinal)
            .ToArray();
        builder.Block(() =>
        {
            builder.Line($"private protected {typeName}() {{ }}");
            builder.Line();
            foreach (var member in members)
            {
                var variantName = CSharpIdentifier.TypeName(member.Name);
                var valueType = GetValueType(
                    model,
                    member.Target,
                    nullable: false,
                    currentNamespace: shape.Id.Namespace,
                    baseNamespace: options.BaseNamespace
                );
                builder.Line($"public sealed partial record class {variantName} : {typeName}");
                builder.Block(() =>
                {
                    builder.Line($"public {variantName}({valueType} value)");
                    builder.Block(() =>
                    {
                        builder.Line(
                            $"Value = {GetUnionValueAssignment(model, member.Target, "value")};"
                        );
                    });
                    builder.Line();
                    builder.Line($"public {valueType} Value {{ get; }}");
                });
                builder.Line();
                builder.Line($"public static {typeName} From{variantName}({valueType} value)");
                builder.Block(() =>
                {
                    builder.Line($"return new {variantName}(value);");
                });
                builder.Line();
            }

            builder.Line("public sealed partial record class Unknown : " + typeName);
            builder.Block(() =>
            {
                builder.Line("public Unknown(string tag, Document value)");
                builder.Block(() =>
                {
                    builder.Line("Tag = tag ?? throw new ArgumentNullException(nameof(tag));");
                    builder.Line("Value = value;");
                });
                builder.Line();
                builder.Line("public string Tag { get; }");
                builder.Line("public Document Value { get; }");
            });
            builder.Line();
            builder.Line($"public static {typeName} FromUnknown(string tag, Document value)");
            builder.Block(() =>
            {
                builder.Line("return new Unknown(tag, value);");
            });
            builder.Line();

            builder.Line("public T Match<T>(");
            builder.Indented(() =>
            {
                foreach (var member in members)
                {
                    var parameterName = CSharpIdentifier.ParameterName(member.Name);
                    var valueType = GetValueType(
                        model,
                        member.Target,
                        nullable: false,
                        currentNamespace: shape.Id.Namespace,
                        baseNamespace: options.BaseNamespace
                    );
                    builder.Line($"Func<{valueType}, T> {parameterName},");
                }

                builder.Line("Func<string, Document, T> unknown)");
            });
            builder.Block(() =>
            {
                foreach (var member in members)
                {
                    var parameterName = CSharpIdentifier.ParameterName(member.Name);
                    builder.Line($"ArgumentNullException.ThrowIfNull({parameterName});");
                }

                builder.Line("ArgumentNullException.ThrowIfNull(unknown);");
                builder.Line();
                builder.Line("return this switch");
                builder.Block(
                    () =>
                    {
                        foreach (var member in members)
                        {
                            var variantName = CSharpIdentifier.TypeName(member.Name);
                            var parameterName = CSharpIdentifier.ParameterName(member.Name);
                            builder.Line($"{variantName} value => {parameterName}(value.Value),");
                        }

                        builder.Line("Unknown value => unknown(value.Tag, value.Value),");
                        builder.Line(
                            "_ => throw new InvalidOperationException(\"Unknown union variant.\"),"
                        );
                    },
                    closingSuffix: ";"
                );
            });
        });
        return builder.ToString();
    }

    private static string GetUnionValueAssignment(
        SmithyModel model,
        ShapeId target,
        string parameterName
    )
    {
        return IsReferenceType(model, target)
            ? $"{parameterName} ?? throw new ArgumentNullException(nameof({parameterName}))"
            : parameterName;
    }

    private static void AppendConstructor(
        CSharpWriter builder,
        SmithyModel model,
        ModelShape shape,
        string typeName,
        CSharpGenerationOptions options,
        string? baseCall
    )
    {
        var members = shape
            .Members.Values.OrderBy(member => member.Name, StringComparer.Ordinal)
            .ToArray();
        builder.Write($"public {typeName}(");
        if (baseCall is not null)
        {
            builder.Write("string? message = null");
            if (members.Length > 0)
            {
                builder.Write(", ");
            }
        }

        builder.Write(
            string.Join(
                ", ",
                members.Select(member =>
                    GetParameter(model, shape, member, shape.Id.Namespace, options)
                )
            )
        );
        builder.Line(")");
        if (baseCall is not null)
        {
            builder.Indented(() =>
            {
                builder.Line($": {baseCall}");
            });
        }

        builder.Block(() =>
        {
            foreach (var member in members)
            {
                var propertyName = CSharpIdentifier.PropertyName(member.Name);
                var parameterName = CSharpIdentifier.ParameterName(member.Name);
                builder.Line(
                    $"{propertyName} = {GetAssignment(model, shape, member, parameterName, shape.Id.Namespace, options)};"
                );
            }
        });

        if (members.Length > 0)
        {
            builder.Line();
        }
    }

    private static void AppendErrorConstructor(
        CSharpWriter builder,
        SmithyModel model,
        ModelShape shape,
        string typeName,
        CSharpGenerationOptions options,
        MemberShape? messageMember
    )
    {
        var members = GetSortedMembers(shape, excludedMember: messageMember).ToArray();
        builder.Write($"public {typeName}(string? message = null");
        if (members.Length > 0)
        {
            builder.Write(", ");
        }

        builder.Write(
            string.Join(
                ", ",
                members.Select(member =>
                    GetParameter(model, shape, member, shape.Id.Namespace, options)
                )
            )
        );
        builder.Line(")");
        builder.Indented(() =>
        {
            builder.Line(": base(message)");
        });

        builder.Block(() =>
        {
            foreach (var member in members)
            {
                var propertyName = CSharpIdentifier.PropertyName(member.Name);
                var parameterName = CSharpIdentifier.ParameterName(member.Name);
                builder.Line(
                    $"{propertyName} = {GetAssignment(model, shape, member, parameterName, shape.Id.Namespace, options)};"
                );
            }
        });

        if (members.Length > 0)
        {
            builder.Line();
        }
    }

    private static void AppendProperties(
        CSharpWriter builder,
        SmithyModel model,
        ModelShape shape,
        CSharpGenerationOptions options,
        MemberShape? excludedMember = null
    )
    {
        foreach (var member in GetSortedMembers(shape, excludedMember))
        {
            var propertyName = CSharpIdentifier.PropertyName(member.Name);
            var propertyType = GetMemberType(model, shape, member, shape.Id.Namespace, options);
            builder.Line($"public {propertyType} {propertyName} {{ get; }}");
        }
    }

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
        if (options.NullabilityMode == CSharpNullabilityMode.NonAuthoritative)
        {
            if (container.Traits.Has(SmithyPrelude.InputTrait) || member.IsClientOptional)
            {
                return true;
            }
        }

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

        if (
            options.NullabilityMode == CSharpNullabilityMode.NonAuthoritative
            && (container.Traits.Has(SmithyPrelude.InputTrait) || member.IsClientOptional)
        )
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

    private static CSharpWriter CreateFileBuilder(ModelShape shape, CSharpGenerationOptions options)
    {
        var builder = new CSharpWriter();
        builder.Line("// <auto-generated />");
        builder.Line("#nullable enable");
        builder.Line();
        builder.Line("using System;");
        builder.Line("using System.Collections.Generic;");
        builder.Line("using System.Linq;");
        builder.Line("using Smithy.NET.Core;");
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
}

#pragma warning restore CA1305
