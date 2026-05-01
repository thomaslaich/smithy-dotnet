using System.Globalization;
using System.Text.RegularExpressions;
using Nest.Text;
using NSmithy.CodeGeneration.Model;
using NSmithy.Core;
using NSmithy.Core.Traits;

namespace NSmithy.CodeGeneration.CSharp;

#pragma warning disable CA1305 // Source text is assembled from Smithy identifiers and explicitly formatted literals.

public sealed partial class CSharpShapeGenerator
{
    private static string GenerateStructure(SmithyModel model, ModelShape shape, CSharpGenerationOptions options)
    {
        var _ = CreateTextFileBuilder(shape, options);
        var typeName = GetTypeName(shape.Id);
        AddShapeAttributes(_, shape);
        _.L($"public sealed partial record class {typeName}")
            .B(_ =>
            {
                AddConstructor(_, model, shape, typeName, options, baseCall: null);
                AddProperties(_, model, shape, options);
            });
        return FormatGeneratedText(_);
    }

    private static string GenerateError(SmithyModel model, ModelShape shape, CSharpGenerationOptions options)
    {
        var _ = CreateTextFileBuilder(shape, options);
        var typeName = GetTypeName(shape.Id);
        AddShapeAttributes(_, shape);
        _.L($"public sealed partial class {typeName} : Exception")
            .B(_ =>
            {
                var messageMember = GetErrorMessageMember(shape);
                AddErrorConstructor(_, model, shape, typeName, options, messageMember);
                AddErrorMessageProperty(_, messageMember);
                AddProperties(_, model, shape, options, messageMember);
            });
        return FormatGeneratedText(_);
    }

    private static string GenerateList(SmithyModel model, ModelShape shape, CSharpGenerationOptions options)
    {
        var _ = CreateTextFileBuilder(shape, options);
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
        AddShapeAttributes(_, shape);
        _.L($"public sealed partial record class {typeName}")
            .B(_ =>
            {
                _.L($"public {typeName}(IEnumerable<{memberType}> values)")
                    .B(_ =>
                    {
                        _.L("ArgumentNullException.ThrowIfNull(values);");
                        _.L("Values = Array.AsReadOnly(values.ToArray());");
                    });
                _.L();
                AddMemberAttributes(_, member, isSparse: shape.Traits.Has(SmithyPrelude.SparseTrait));
                _.L($"public IReadOnlyList<{memberType}> Values {{ get; }}");
            });
        return FormatGeneratedText(_);
    }

    private static string GenerateMap(SmithyModel model, ModelShape shape, CSharpGenerationOptions options)
    {
        var _ = CreateTextFileBuilder(shape, options);
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

        AddShapeAttributes(_, shape);
        _.L($"public sealed partial record class {typeName}")
            .B(builder =>
            {
                builder
                    .L($"public {typeName}(IReadOnlyDictionary<{keyType}, {valueType}> values)")
                    .B(_ =>
                    {
                        _.L("ArgumentNullException.ThrowIfNull(values);");
                        _.L(
                            $"Values = new System.Collections.ObjectModel.ReadOnlyDictionary<{keyType}, {valueType}>(new Dictionary<{keyType}, {valueType}>(values));"
                        );
                    });
                builder.L();
                AddMemberAttributes(builder, value, isSparse: shape.Traits.Has(SmithyPrelude.SparseTrait));
                builder.L($"public IReadOnlyDictionary<{keyType}, {valueType}> Values {{ get; }}");
            });
        return FormatGeneratedText(_);
    }

    private static string GenerateStringEnum(ModelShape shape, CSharpGenerationOptions options)
    {
        var _ = CreateTextFileBuilder(shape, options);
        var typeName = GetTypeName(shape.Id);
        AddShapeAttributes(_, shape);
        _.L($"public readonly partial record struct {typeName}(string Value)")
            .B(builder =>
            {
                foreach (var member in shape.Members.Values.OrderBy(member => member.Name, StringComparer.Ordinal))
                {
                    var propertyName = CSharpIdentifier.PropertyName(member.Name);
                    var value = member.Traits.GetValueOrDefault(SmithyPrelude.EnumValueTrait)?.AsString() ?? member.Name;
                    builder.L($"[SmithyEnumValue({FormatString(value)})]");
                    builder.L($"public static {typeName} {propertyName} {{ get; }} = new({FormatString(value)});");
                }

                builder.L();
                builder
                    .L("public override string ToString()")
                    .B(_ =>
                    {
                        _.L("return Value;");
                    });
            });
        return FormatGeneratedText(_);
    }

    private static string GenerateIntEnum(ModelShape shape, CSharpGenerationOptions options)
    {
        var _ = CreateTextFileBuilder(shape, options);
        var typeName = GetTypeName(shape.Id);
        AddShapeAttributes(_, shape);
        _.L($"public enum {typeName}")
            .B(builder =>
            {
                foreach (var member in shape.Members.Values.OrderBy(member => member.Name, StringComparer.Ordinal))
                {
                    var propertyName = CSharpIdentifier.PropertyName(member.Name);
                    var value = member.Traits.GetValueOrDefault(SmithyPrelude.EnumValueTrait)?.AsNumber();
                    var suffix = value is null ? string.Empty : string.Create(CultureInfo.InvariantCulture, $" = {(int)value.Value}");
                    if (value is not null)
                    {
                        builder.L($"[SmithyEnumValue({FormatString(((int)value.Value).ToString(CultureInfo.InvariantCulture))})]");
                    }

                    builder.L($"{propertyName}{suffix},");
                }
            });
        return FormatGeneratedText(_);
    }

    private static string GenerateUnion(SmithyModel model, ModelShape shape, CSharpGenerationOptions options)
    {
        var _ = CreateTextFileBuilder(shape, options);
        var typeName = GetTypeName(shape.Id);
        AddShapeAttributes(_, shape);
        var members = GetConstructorMembers(model, shape, options);
        _.L($"public abstract partial record class {typeName}")
            .B(builder =>
            {
                builder.L($"private protected {typeName}() {{ }}");
                builder.L();
                foreach (var member in members)
                {
                    AddUnionVariant(builder, model, shape, member, options, typeName);
                }

                AddUnionUnknownVariant(builder, typeName);
                builder.L();
                AddUnionUnknownFactory(builder, typeName);
                builder.L();
                AddUnionMatchMethod(builder, model, members, options);
            });
        return FormatGeneratedText(_);
    }

    private static void AddUnionVariant(
        ITextBuilder _,
        SmithyModel model,
        ModelShape shape,
        MemberShape member,
        CSharpGenerationOptions options,
        string typeName
    )
    {
        var variantName = CSharpIdentifier.TypeName(member.Name);
        var valueType = GetValueType(
            model,
            member.Target,
            nullable: false,
            currentNamespace: string.Empty,
            baseNamespace: options.BaseNamespace
        );
        AddMemberAttributes(_, member, isSparse: false);
        _.L($"public sealed partial record class {variantName} : {typeName}")
            .B(builder =>
            {
                builder
                    .L($"public {variantName}({valueType} value)")
                    .B(_ =>
                    {
                        _.L($"Value = {GetUnionValueAssignment(model, member.Target, "value")};");
                    });
                builder.L();
                builder.L($"public {valueType} Value {{ get; }}");
            });
        _.L();
        _.L($"public static {typeName} From{variantName}({valueType} value)")
            .B(_ =>
            {
                _.L($"return new {variantName}(value);");
            });
        _.L();
    }

    private static void AddUnionUnknownVariant(ITextBuilder _, string typeName)
    {
        _.L($"public sealed partial record class Unknown : {typeName}")
            .B(builder =>
            {
                builder
                    .L("public Unknown(string tag, Document value)")
                    .B(_ =>
                    {
                        _.L("Tag = tag ?? throw new ArgumentNullException(nameof(tag));");
                        _.L("Value = value;");
                    });
                builder.L();
                builder.L("public string Tag { get; }");
                builder.L("public Document Value { get; }");
            });
    }

    private static void AddUnionUnknownFactory(ITextBuilder _, string typeName)
    {
        _.L($"public static {typeName} FromUnknown(string tag, Document value)")
            .B(_ =>
            {
                _.L("return new Unknown(tag, value);");
            });
    }

    private static void AddUnionMatchMethod(
        ITextBuilder _,
        SmithyModel model,
        IReadOnlyList<MemberShape> members,
        CSharpGenerationOptions options
    )
    {
        _.L("public T Match<T>(")
            .B(
                builder =>
                {
                    foreach (var member in members)
                    {
                        var parameterName = CSharpIdentifier.ParameterName(member.Name);
                        var valueType = GetValueType(
                            model,
                            member.Target,
                            nullable: false,
                            currentNamespace: string.Empty,
                            baseNamespace: options.BaseNamespace
                        );
                        builder.L($"Func<{valueType}, T> {parameterName},");
                    }

                    builder.L("Func<string, Document, T> unknown)");
                },
                ConfigureTextBlock(BlockStyle.IndentOnly)
            );
        _.L("{")
            .B(
                builder =>
                {
                    foreach (var member in members)
                    {
                        var parameterName = CSharpIdentifier.ParameterName(member.Name);
                        builder.L($"ArgumentNullException.ThrowIfNull({parameterName});");
                    }

                    builder.L("ArgumentNullException.ThrowIfNull(unknown);");
                    builder.L();
                    builder.L("return this switch");
                    builder
                        .L("{")
                        .B(
                            switchBuilder =>
                            {
                                foreach (var member in members)
                                {
                                    var variantName = CSharpIdentifier.TypeName(member.Name);
                                    var parameterName = CSharpIdentifier.ParameterName(member.Name);
                                    switchBuilder.L($"{variantName} value => {parameterName}(value.Value),");
                                }

                                switchBuilder.L("Unknown value => unknown(value.Tag, value.Value),");
                                switchBuilder.L("_ => throw new InvalidOperationException(\"Unknown union variant.\"),");
                            },
                            ConfigureTextBlock(BlockStyle.IndentOnly)
                        );
                    builder.L("};");
                },
                ConfigureTextBlock(BlockStyle.IndentOnly)
            );
        _.L("}");
    }

    private static string GetUnionValueAssignment(SmithyModel model, ShapeId target, string parameterName)
    {
        return IsReferenceType(model, target)
            ? $"{parameterName} ?? throw new ArgumentNullException(nameof({parameterName}))"
            : parameterName;
    }

    private static void AddConstructor(
        ITextBuilder _,
        SmithyModel model,
        ModelShape shape,
        string typeName,
        CSharpGenerationOptions options,
        string? baseCall
    )
    {
        var members = GetConstructorMembers(model, shape, options);
        if (members.Length == 0)
        {
            _.L(BuildConstructorDeclaration(model, shape, typeName, members, options, baseCall));
            _.L("{");
            _.L("}");
            return;
        }

        var constructor = _.L(BuildConstructorDeclaration(model, shape, typeName, members, options, baseCall));

        constructor.B(builder => AddConstructorAssignments(builder, model, shape, members, shape.Id.Namespace, options), options_act: null);

        if (members.Length > 0)
        {
            _.L();
        }
    }

    private static string BuildConstructorSignature(
        SmithyModel model,
        ModelShape shape,
        string typeName,
        IReadOnlyList<MemberShape> members,
        CSharpGenerationOptions options,
        string? baseCall
    )
    {
        var signature = $"public {typeName}(";
        if (baseCall is not null)
        {
            signature += "string? message = null";
            if (members.Count > 0)
            {
                signature += ", ";
            }
        }

        signature += string.Join(", ", members.Select(member => GetParameter(model, shape, member, shape.Id.Namespace, options)));
        signature += ")";
        return signature;
    }

    private static string BuildConstructorDeclaration(
        SmithyModel model,
        ModelShape shape,
        string typeName,
        IReadOnlyList<MemberShape> members,
        CSharpGenerationOptions options,
        string? baseCall
    )
    {
        var signature = BuildConstructorSignature(model, shape, typeName, members, options, baseCall);
        return baseCall is null ? signature : $"{signature}{Environment.NewLine}    : {baseCall}";
    }

    private static void AddErrorConstructor(
        ITextBuilder _,
        SmithyModel model,
        ModelShape shape,
        string typeName,
        CSharpGenerationOptions options,
        MemberShape? messageMember
    )
    {
        var members = GetConstructorMembers(model, shape, options, messageMember);
        var hasRequiredMembers = members.Any(member => !HasOptionalConstructorParameter(model, shape, member, options));
        if (members.Length == 0)
        {
            _.L($"public {typeName}(string? message{(hasRequiredMembers ? string.Empty : " = null")})");
            _.L("    : base(message)");
            _.L("{");
            _.L("}");
            return;
        }

        var constructor = _.L(
            $"public {typeName}(string? message{(hasRequiredMembers ? string.Empty : " = null")}{(members.Length > 0 ? ", " : string.Empty)}{string.Join(", ", members.Select(member => GetParameter(model, shape, member, shape.Id.Namespace, options)))}){Environment.NewLine}    : base(message)"
        );
        constructor.B(builder => AddConstructorAssignments(builder, model, shape, members, shape.Id.Namespace, options), options_act: null);

        if (members.Length > 0)
        {
            _.L();
        }
    }

    private static void AddConstructorAssignments(
        ITextBuilder _,
        SmithyModel model,
        ModelShape shape,
        IReadOnlyList<MemberShape> members,
        string currentNamespace,
        CSharpGenerationOptions options
    )
    {
        foreach (var member in members)
        {
            var propertyName = CSharpIdentifier.PropertyName(member.Name);
            var parameterName = CSharpIdentifier.ParameterName(member.Name);
            _.L($"{propertyName} = {GetAssignment(model, shape, member, parameterName, currentNamespace, options)};");
        }
    }

    private static void AddErrorMessageProperty(ITextBuilder _, MemberShape? messageMember)
    {
        if (messageMember is null)
        {
            return;
        }

        AddMemberAttributes(_, messageMember, isSparse: false);
        _.L("public override string Message => base.Message;");
        _.L();
    }

    private static MemberShape[] GetConstructorMembers(
        SmithyModel model,
        ModelShape shape,
        CSharpGenerationOptions options,
        MemberShape? excludedMember = null
    )
    {
        return shape
            .Members.Values.Where(member => !ReferenceEquals(member, excludedMember))
            .OrderBy(member => HasOptionalConstructorParameter(model, shape, member, options) ? 1 : 0)
            .ThenBy(member => member.Name, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool HasOptionalConstructorParameter(
        SmithyModel model,
        ModelShape container,
        MemberShape member,
        CSharpGenerationOptions options
    )
    {
        _ = model;
        return IsNullableMember(container, member, options) || GetEffectiveDefaultValue(container, member, options) is not null;
    }

    private static void AddProperties(
        ITextBuilder _,
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
            AddMemberAttributes(_, member, isSparse: IsSparseTarget(model, member.Target));
            _.L($"public {propertyType} {propertyName} {{ get; }}");
        }
    }

    private static void AddShapeAttributes(ITextBuilder _, ModelShape shape)
    {
        _.L($"[SmithyShape({FormatString(shape.Id.ToString())}, ShapeKind.{shape.Kind})]");
        AddTraitAttributes(_, shape.Traits);
    }

    private static void AddMemberAttributes(ITextBuilder _, MemberShape member, bool isSparse)
    {
        var arguments = new List<string> { FormatString(member.Name), FormatString(member.Target.ToString()) };
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

        _.L($"[SmithyMember({string.Join(", ", arguments)})]");
        AddTraitAttributes(_, member.Traits);
    }

    private static void AddTraitAttributes(ITextBuilder _, TraitCollection traits)
    {
        foreach (var trait in traits.OrderBy(trait => trait.Key.ToString(), StringComparer.Ordinal))
        {
            var value = GetTraitAttributeValue(trait.Value);
            var valueInitializer = value is null ? string.Empty : $", Value = {FormatString(value)}";
            _.L($"[SmithyTrait({FormatString(trait.Key.ToString())}{valueInitializer})]");
        }
    }

    private static TextBuilder CreateTextFileBuilder(
        ModelShape shape,
        CSharpGenerationOptions options,
        IReadOnlyList<string>? extraUsings = null
    )
    {
        var _ = (TextBuilder)
            TextBuilder.Create(
                new TextBuilderOptions
                {
                    BlockStyle = BlockStyle.CurlyBraces,
                    IndentChar = ' ',
                    IndentSize = 4,
                    AddImplicitLineBreaks = false,
                }
            );
        _.L("// <auto-generated />");
        _.L("#nullable enable");
        _.L();
        _.L("using System;");
        _.L("using System.Collections.Generic;");
        _.L("using System.Linq;");
        _.L("using NSmithy.Core;");
        _.L("using NSmithy.Core.Annotations;");
        foreach (var @namespace in extraUsings ?? [])
        {
            _.L($"using {@namespace};");
        }

        _.L();
        _.L($"namespace {CSharpIdentifier.Namespace(shape.Id.Namespace, options.BaseNamespace)};");
        _.L();
        return _;
    }

    private static string FormatGeneratedText(TextBuilder builder)
    {
        return Regex.Replace(builder.ToString(), "(?m)^[ \t]+$", string.Empty);
    }

    private static Action<TextBuilderOptions> ConfigureTextBlock(BlockStyle blockStyle)
    {
        return options =>
        {
            options.BlockStyle = blockStyle;
            options.IndentChar = ' ';
            options.IndentSize = 4;
            options.AddImplicitLineBreaks = false;
        };
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
