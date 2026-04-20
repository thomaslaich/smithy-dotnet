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
                _ => throw new NotSupportedException(
                    $"Protocol test literal generation for '{target}' is not supported."
                ),
            };
        }

        var shape = model.GetShape(target);
        return shape.Kind switch
        {
            ShapeKind.Structure => CreateStructure(model, shape, value, currentNamespace, options),
            ShapeKind.Map => CreateMap(model, shape, value, currentNamespace, options),
            ShapeKind.Enum =>
                $"new {GetTypeReference(target, currentNamespace, options)}({FormatString(value.AsString())})",
            ShapeKind.IntEnum =>
                $"({GetTypeReference(target, currentNamespace, options)}){value.AsNumber().ToString(CultureInfo.InvariantCulture)}",
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
                _ => throw new NotSupportedException(
                    $"Protocol test assertion generation for '{target}' is not supported."
                ),
            };
        }

        var shape = model.GetShape(target);
        if (shape.Kind == ShapeKind.Map)
        {
            return CreateMapEqualityAssertion(shape, actualExpression, expected, failureContext);
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
                        $"{actualExpression}.{CSharpIdentifier.PropertyName(member.Name)}",
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
        var arguments = shape
            .Members.Values.OrderBy(member => member.Name, StringComparer.Ordinal)
            .Select(member =>
                properties.TryGetValue(member.Name, out var memberValue)
                    ? CreateValue(model, member.Target, memberValue, shape.Id.Namespace, options)
                    : "null"
            );
        return $"new {GetTypeReference(shape.Id, currentNamespace, options)}({string.Join(", ", arguments)})";
    }

    private static string CreateMap(
        SmithyModel model,
        ModelShape shape,
        Document value,
        string currentNamespace,
        CSharpGenerationOptions options
    )
    {
        var mapValue = shape.Members["value"];
        var entries = value
            .AsObject()
            .Select(item =>
                $"[{FormatString(item.Key)}] = {CreateValue(model, mapValue.Target, item.Value, shape.Id.Namespace, options)}"
            );
        return $"new {GetTypeReference(shape.Id, currentNamespace, options)}(new Dictionary<string, string> {{ {string.Join(", ", entries)} }})";
    }

    private static string CreateMapEqualityAssertion(
        ModelShape shape,
        string actualExpression,
        Document expected,
        string failureContext
    )
    {
        if (shape.Members["value"].Target != ShapeId.Parse("smithy.api#String"))
        {
            throw new NotSupportedException(
                $"Protocol test map assertion generation for '{shape.Id}' is not supported."
            );
        }

        return string.Join(
            Environment.NewLine,
            expected
                .AsObject()
                .Select(item =>
                    $"if (!{actualExpression}!.Values.TryGetValue({FormatString(item.Key)}, out var {CSharpIdentifier.ParameterName(item.Key)}Value) || {CSharpIdentifier.ParameterName(item.Key)}Value != {FormatString(item.Value.AsString())}) throw new InvalidOperationException({FormatString(failureContext)});"
                )
        );
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
