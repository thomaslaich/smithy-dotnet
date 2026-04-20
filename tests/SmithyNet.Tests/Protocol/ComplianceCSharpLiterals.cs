using System.Globalization;
using SmithyNet.CodeGeneration.CSharp;
using SmithyNet.CodeGeneration.Model;
using SmithyNet.Core;

namespace SmithyNet.Tests.Protocol;

internal static class ComplianceCSharpLiterals
{
    public static string CreateValue(
        SmithyModel model,
        ShapeId target,
        Document value,
        string currentNamespace,
        CSharpGenerationOptions options
    )
    {
        if (value.Kind == DocumentKind.Null)
        {
            return "null!";
        }

        if (target.Namespace == SmithyPrelude.Namespace)
        {
            return target.Name switch
            {
                "String" => FormatString(value.AsString()),
                "Boolean" => value.AsBoolean() ? "true" : "false",
                "Byte" or "Short" or "Integer" or "Long" => value
                    .AsNumber()
                    .ToString(CultureInfo.InvariantCulture),
                "Float" => $"{value.AsNumber().ToString(CultureInfo.InvariantCulture)}f",
                "Double" => $"{value.AsNumber().ToString(CultureInfo.InvariantCulture)}d",
                "Timestamp" =>
                    $"DateTimeOffset.FromUnixTimeSeconds({value.AsNumber().ToString(CultureInfo.InvariantCulture)})",
                _ => throw new NotSupportedException(
                    $"Protocol test literal generation for '{target}' is not supported."
                ),
            };
        }

        var shape = model.GetShape(target);
        return shape.Kind switch
        {
            ShapeKind.Structure => CreateStructure(model, shape, value, currentNamespace, options),
            ShapeKind.List or ShapeKind.Set => CreateList(
                model,
                shape,
                value,
                currentNamespace,
                options
            ),
            ShapeKind.Map => CreateMap(model, shape, value, currentNamespace, options),
            ShapeKind.Enum =>
                $"new {GetTypeReference(target, currentNamespace, options)}({FormatString(value.AsString())})",
            ShapeKind.IntEnum =>
                $"({GetTypeReference(target, currentNamespace, options)}){value.AsNumber().ToString(CultureInfo.InvariantCulture)}",
            ShapeKind.Union => CreateUnion(model, shape, value, currentNamespace, options),
            _ => throw new NotSupportedException(
                $"Protocol test literal generation for shape kind '{shape.Kind}' is not supported."
            ),
        };
    }

    public static string CreateEqualityAssertion(
        SmithyModel model,
        ShapeId target,
        string actualExpression,
        Document expected,
        string currentNamespace,
        CSharpGenerationOptions options,
        string failureContext
    )
    {
        if (target.Namespace == SmithyPrelude.Namespace)
        {
            return target.Name switch
            {
                "String" =>
                    $"if ({actualExpression} != {FormatString(expected.AsString())}) throw new InvalidOperationException({FormatString(failureContext)} + \": \" + {actualExpression});",
                "Boolean" =>
                    $"if ({actualExpression} != {(expected.AsBoolean() ? "true" : "false")}) throw new InvalidOperationException({FormatString(failureContext)} + \": \" + {actualExpression});",
                "Byte" or "Short" or "Integer" or "Long" =>
                    $"if ({actualExpression} != {expected.AsNumber().ToString(CultureInfo.InvariantCulture)}) throw new InvalidOperationException({FormatString(failureContext)} + \": \" + {actualExpression});",
                "Float" =>
                    $"if (Math.Abs({actualExpression} - {expected.AsNumber().ToString(CultureInfo.InvariantCulture)}f) > 0.0001f) throw new InvalidOperationException({FormatString(failureContext)} + \": \" + {actualExpression});",
                "Double" =>
                    $"if (Math.Abs({actualExpression} - {expected.AsNumber().ToString(CultureInfo.InvariantCulture)}d) > 0.0001d) throw new InvalidOperationException({FormatString(failureContext)} + \": \" + {actualExpression});",
                "Timestamp" =>
                    $"if ({actualExpression} != DateTimeOffset.FromUnixTimeSeconds({expected.AsNumber().ToString(CultureInfo.InvariantCulture)})) throw new InvalidOperationException({FormatString(failureContext)} + \": \" + {actualExpression});",
                _ => throw new NotSupportedException(
                    $"Protocol test assertion generation for '{target}' is not supported."
                ),
            };
        }

        var shape = model.GetShape(target);
        if (shape.Kind == ShapeKind.Map)
        {
            return CreateMapEqualityAssertion(
                model,
                shape,
                actualExpression,
                expected,
                options,
                failureContext
            );
        }

        if (shape.Kind == ShapeKind.List || shape.Kind == ShapeKind.Set)
        {
            return CreateListEqualityAssertion(
                model,
                shape,
                actualExpression,
                expected,
                options,
                failureContext
            );
        }

        if (shape.Kind == ShapeKind.Enum)
        {
            return $"if ({actualExpression}.Value != {FormatString(expected.AsString())}) throw new InvalidOperationException({FormatString(failureContext)} + \": \" + {actualExpression});";
        }

        if (shape.Kind == ShapeKind.IntEnum)
        {
            return $"if ((int){actualExpression} != {expected.AsNumber().ToString(CultureInfo.InvariantCulture)}) throw new InvalidOperationException({FormatString(failureContext)} + \": \" + {actualExpression});";
        }

        if (shape.Kind == ShapeKind.Union)
        {
            return CreateUnionEqualityAssertion(
                model,
                shape,
                actualExpression,
                expected,
                options,
                failureContext
            );
        }

        if (shape.Kind != ShapeKind.Structure)
        {
            throw new NotSupportedException(
                $"Protocol test assertion generation for shape kind '{shape.Kind}' is not supported."
            );
        }

        var expectedProperties = expected.AsObject();
        return string.Join(
            Environment.NewLine,
            shape
                .Members.Values.OrderBy(member => member.Name, StringComparer.Ordinal)
                .Where(member => expectedProperties.ContainsKey(member.Name))
                .Select(member =>
                    CreateEqualityAssertion(
                        model,
                        member.Target,
                        $"{actualExpression}!.{CSharpIdentifier.PropertyName(member.Name)}",
                        expectedProperties[member.Name],
                        shape.Id.Namespace,
                        options,
                        $"{failureContext}.{member.Name}"
                    )
                )
        );
    }

    private static string CreateStructure(
        SmithyModel model,
        ModelShape shape,
        Document value,
        string currentNamespace,
        CSharpGenerationOptions options
    )
    {
        var properties = value.AsObject();
        var arguments = GetConstructorMembers(shape)
            .Select(member =>
                properties.TryGetValue(member.Name, out var memberValue)
                    ? CreateValue(model, member.Target, memberValue, shape.Id.Namespace, options)
                    : "null"
            );
        return $"new {GetTypeReference(shape.Id, currentNamespace, options)}({string.Join(", ", arguments)})";
    }

    private static IEnumerable<MemberShape> GetConstructorMembers(ModelShape shape)
    {
        return shape
            .Members.Values.OrderBy(member => member.IsRequired ? 0 : 1)
            .ThenBy(member => member.Name, StringComparer.Ordinal);
    }

    private static string CreateList(
        SmithyModel model,
        ModelShape shape,
        Document value,
        string currentNamespace,
        CSharpGenerationOptions options
    )
    {
        var member = shape.Members["member"];
        var memberType = GetValueType(model, member.Target, shape.Id.Namespace, options);
        var items = value
            .AsArray()
            .Select(item => CreateValue(model, member.Target, item, shape.Id.Namespace, options));
        return $"new {GetTypeReference(shape.Id, currentNamespace, options)}(new {memberType}[] {{ {string.Join(", ", items)} }})";
    }

    private static string CreateMap(
        SmithyModel model,
        ModelShape shape,
        Document value,
        string currentNamespace,
        CSharpGenerationOptions options
    )
    {
        var key = shape.Members["key"];
        var mapValue = shape.Members["value"];
        var keyType = GetValueType(model, key.Target, shape.Id.Namespace, options);
        var valueType = GetValueType(model, mapValue.Target, shape.Id.Namespace, options);
        var entries = value
            .AsObject()
            .Select(item =>
                $"[{FormatString(item.Key)}] = {CreateValue(model, mapValue.Target, item.Value, shape.Id.Namespace, options)}"
            );
        return $"new {GetTypeReference(shape.Id, currentNamespace, options)}(new Dictionary<{keyType}, {valueType}> {{ {string.Join(", ", entries)} }})";
    }

    private static string CreateUnion(
        SmithyModel model,
        ModelShape shape,
        Document value,
        string currentNamespace,
        CSharpGenerationOptions options
    )
    {
        var properties = value.AsObject();
        if (properties.Count != 1)
        {
            throw new NotSupportedException(
                $"Protocol test union literal for '{shape.Id}' must contain exactly one member."
            );
        }

        var property = properties.Single();
        var member = shape.Members[property.Key];
        var typeReference = GetTypeReference(shape.Id, currentNamespace, options);
        var variantName = CSharpIdentifier.TypeName(member.Name);
        var variantValue = CreateValue(
            model,
            member.Target,
            property.Value,
            shape.Id.Namespace,
            options
        );
        return $"{typeReference}.From{variantName}({variantValue})";
    }

    private static string CreateMapEqualityAssertion(
        SmithyModel model,
        ModelShape shape,
        string actualExpression,
        Document expected,
        CSharpGenerationOptions options,
        string failureContext
    )
    {
        var value = shape.Members["value"];
        return string.Join(
            Environment.NewLine,
            expected
                .AsObject()
                .Select(
                    (item, index) =>
                        $$"""
                    if (!{{actualExpression}}!.Values.TryGetValue({{FormatString(item.Key)}}, out var mapValue{{index.ToString(CultureInfo.InvariantCulture)}}))
                    {
                        throw new InvalidOperationException({{FormatString(failureContext)}});
                    }
                    {{CreateEqualityAssertion(
                        model,
                        value.Target,
                        $"mapValue{index.ToString(CultureInfo.InvariantCulture)}",
                        item.Value,
                        shape.Id.Namespace,
                        options,
                        $"{failureContext}.{item.Key}"
                    )}}
                    """
                )
        );
    }

    private static string CreateListEqualityAssertion(
        SmithyModel model,
        ModelShape shape,
        string actualExpression,
        Document expected,
        CSharpGenerationOptions options,
        string failureContext
    )
    {
        var member = shape.Members["member"];
        var items = expected.AsArray();
        var assertions = new List<string>
        {
            $"if ({actualExpression}!.Values.Count != {items.Count}) throw new InvalidOperationException({FormatString(failureContext)});",
        };
        assertions.AddRange(
            items.Select(
                (item, index) =>
                    CreateEqualityAssertion(
                        model,
                        member.Target,
                        $"{actualExpression}!.Values[{index.ToString(CultureInfo.InvariantCulture)}]",
                        item,
                        shape.Id.Namespace,
                        options,
                        $"{failureContext}[{index.ToString(CultureInfo.InvariantCulture)}]"
                    )
            )
        );
        return string.Join(Environment.NewLine, assertions);
    }

    private static string CreateUnionEqualityAssertion(
        SmithyModel model,
        ModelShape shape,
        string actualExpression,
        Document expected,
        CSharpGenerationOptions options,
        string failureContext
    )
    {
        var properties = expected.AsObject();
        if (properties.Count != 1)
        {
            throw new NotSupportedException(
                $"Protocol test union assertion for '{shape.Id}' must contain exactly one member."
            );
        }

        var property = properties.Single();
        var member = shape.Members[property.Key];
        var variantName = CSharpIdentifier.TypeName(member.Name);
        return $$"""
            if ({{actualExpression}} is not {{GetTypeReference(shape.Id, shape.Id.Namespace, options)}}.{{variantName}} unionValue)
            {
                throw new InvalidOperationException({{FormatString(failureContext)}});
            }
            {{CreateEqualityAssertion(
                model,
                member.Target,
                "unionValue.Value",
                property.Value,
                shape.Id.Namespace,
                options,
                $"{failureContext}.{property.Key}"
            )}}
            """;
    }

    private static string GetValueType(
        SmithyModel model,
        ShapeId id,
        string currentNamespace,
        CSharpGenerationOptions options
    )
    {
        if (id.Namespace == SmithyPrelude.Namespace)
        {
            return id.Name switch
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
                _ => throw new NotSupportedException(
                    $"Protocol test value type generation for '{id}' is not supported."
                ),
            };
        }

        _ = model.GetShape(id);
        return GetTypeReference(id, currentNamespace, options);
    }

    private static string GetTypeReference(
        ShapeId id,
        string currentNamespace,
        CSharpGenerationOptions options
    )
    {
        var typeName = CSharpIdentifier.TypeName(id.Name);
        return string.Equals(id.Namespace, currentNamespace, StringComparison.Ordinal)
            ? typeName
            : $"global::{CSharpIdentifier.Namespace(id.Namespace, options.BaseNamespace)}.{typeName}";
    }

    public static string FormatString(string value)
    {
        return string.Concat(
            "\"",
            value
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal)
                .Replace("\r", "\\r", StringComparison.Ordinal)
                .Replace("\n", "\\n", StringComparison.Ordinal),
            "\""
        );
    }
}
