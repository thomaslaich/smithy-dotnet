using System.Globalization;
using SmithyNet.CodeGeneration.Model;
using SmithyNet.Core;
using SmithyNet.Core.Traits;

namespace SmithyNet.CodeGeneration.CSharp;

#pragma warning disable CA1305 // Source text is assembled from Smithy identifiers and explicitly formatted literals.

public sealed partial class CSharpShapeGenerator
{
    private static string GenerateStructure(
        SmithyModel model,
        ModelShape shape,
        CSharpGenerationOptions options
    )
    {
        var builder = CreateFileBuilder(shape, options);
        var typeName = GetTypeName(shape.Id);
        AppendShapeAttributes(builder, shape);
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
        AppendShapeAttributes(builder, shape);
        builder.Line($"public sealed partial class {typeName} : Exception");
        builder.Block(() =>
        {
            var messageMember = GetErrorMessageMember(shape);
            AppendErrorConstructor(builder, model, shape, typeName, options, messageMember);
            AppendErrorMessageProperty(builder, messageMember);
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
        AppendShapeAttributes(builder, shape);
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
            AppendMemberAttributes(
                builder,
                member,
                isSparse: shape.Traits.Has(SmithyPrelude.SparseTrait)
            );
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

        AppendShapeAttributes(builder, shape);
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
            AppendMemberAttributes(
                builder,
                value,
                isSparse: shape.Traits.Has(SmithyPrelude.SparseTrait)
            );
            builder.Line($"public IReadOnlyDictionary<{keyType}, {valueType}> Values {{ get; }}");
        });
        return builder.ToString();
    }

    private static string GenerateStringEnum(ModelShape shape, CSharpGenerationOptions options)
    {
        var builder = CreateFileBuilder(shape, options);
        var typeName = GetTypeName(shape.Id);
        AppendShapeAttributes(builder, shape);
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
                builder.Line($"[SmithyEnumValue({FormatString(value)})]");
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
        AppendShapeAttributes(builder, shape);
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
                if (value is not null)
                {
                    builder.Line(
                        $"[SmithyEnumValue({FormatString(((int)value.Value).ToString(CultureInfo.InvariantCulture))})]"
                    );
                }

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
        AppendShapeAttributes(builder, shape);
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
                AppendMemberAttributes(builder, member, isSparse: false);
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

    private static void AppendErrorMessageProperty(CSharpWriter builder, MemberShape? messageMember)
    {
        if (messageMember is null)
        {
            return;
        }

        AppendMemberAttributes(builder, messageMember, isSparse: false);
        builder.Line("public override string Message => base.Message;");
        builder.Line();
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
            AppendMemberAttributes(builder, member, isSparse: IsSparseTarget(model, member.Target));
            builder.Line($"public {propertyType} {propertyName} {{ get; }}");
        }
    }

    private static void AppendShapeAttributes(CSharpWriter builder, ModelShape shape)
    {
        builder.Line($"[SmithyShape({FormatString(shape.Id.ToString())}, ShapeKind.{shape.Kind})]");
        AppendTraitAttributes(builder, shape.Traits);
    }

    private static void AppendMemberAttributes(
        CSharpWriter builder,
        MemberShape member,
        bool isSparse
    )
    {
        var arguments = new List<string>
        {
            FormatString(member.Name),
            FormatString(member.Target.ToString()),
        };
        if (member.IsRequired)
        {
            arguments.Add("IsRequired = true");
        }

        if (isSparse)
        {
            arguments.Add("IsSparse = true");
        }

        if (member.Traits.GetValueOrDefault(SmithyPrelude.JsonNameTrait) is { } jsonName)
        {
            arguments.Add($"JsonName = {FormatString(jsonName.AsString())}");
        }

        builder.Line($"[SmithyMember({string.Join(", ", arguments)})]");
        AppendTraitAttributes(builder, member.Traits);
    }

    private static void AppendTraitAttributes(CSharpWriter builder, TraitCollection traits)
    {
        foreach (var trait in traits.OrderBy(trait => trait.Key.ToString(), StringComparer.Ordinal))
        {
            var value = GetTraitAttributeValue(trait.Value);
            var valueInitializer = value is null
                ? string.Empty
                : $", Value = {FormatString(value)}";
            builder.Line($"[SmithyTrait({FormatString(trait.Key.ToString())}{valueInitializer})]");
        }
    }

    private static string? GetTraitAttributeValue(Document value)
    {
        return value.Kind switch
        {
            DocumentKind.Null => null,
            DocumentKind.Boolean => value.AsBoolean() ? "true" : "false",
            DocumentKind.Number => value.AsNumber().ToString(CultureInfo.InvariantCulture),
            DocumentKind.String => value.AsString(),
            _ => null,
        };
    }
}

#pragma warning restore CA1305
