using System.Globalization;
using System.Text;
using NSmithy.CodeGeneration.Model;
using NSmithy.Core;

namespace NSmithy.CodeGeneration.Proto;

#pragma warning disable CA1305 // Source text is assembled from Smithy identifiers and explicitly formatted literals.

public sealed class ProtoShapeGenerator
{
    public IReadOnlyList<GeneratedProtoFile> Generate(
        SmithyModel model,
        ProtoGenerationOptions? options = null
    )
    {
        ArgumentNullException.ThrowIfNull(model);
        options ??= new ProtoGenerationOptions();

        return
        [
            .. model
                .Shapes.Values.Where(shape => ShouldGenerate(shape, options))
                .OrderBy(shape => shape.Id.ToString(), StringComparer.Ordinal)
                .Select(shape => GenerateServiceProto(model, shape, options)),
        ];
    }

    private static bool ShouldGenerate(ModelShape shape, ProtoGenerationOptions options)
    {
        return shape.Kind == ShapeKind.Service
            && shape.Traits.Has(SmithyPrelude.GrpcTrait)
            && ShouldGenerateNamespace(shape, options);
    }

    private static bool ShouldGenerateNamespace(ModelShape shape, ProtoGenerationOptions options)
    {
        return options.GeneratedNamespaces is not { Count: > 0 } generatedNamespaces
            || generatedNamespaces.Contains(shape.Id.Namespace, StringComparer.Ordinal);
    }

    private static GeneratedProtoFile GenerateServiceProto(
        SmithyModel model,
        ModelShape service,
        ProtoGenerationOptions options
    )
    {
        var builder = new StringBuilder();
        builder.AppendLine("syntax = \"proto3\";");
        builder.AppendLine(
            $"option csharp_namespace = \"{GetCSharpNamespace(service, options)}\";"
        );
        if (UsesGoogleProtobufEmpty(model, service))
        {
            builder.AppendLine("import \"google/protobuf/empty.proto\";");
        }
        builder.AppendLine();
        builder.AppendLine($"package {service.Id.Namespace};");
        builder.AppendLine();

        var shapes = GetReachableShapes(model, service);
        foreach (
            var shape in shapes.Where(shape =>
                shape.Kind == ShapeKind.Enum || shape.Kind == ShapeKind.IntEnum
            )
        )
        {
            AppendEnum(builder, shape);
            builder.AppendLine();
        }

        foreach (var shape in shapes.Where(shape => shape.Kind == ShapeKind.Structure))
        {
            AppendMessage(model, builder, shape, service.Id.Namespace);
            builder.AppendLine();
        }

        AppendService(model, builder, service);

        return new GeneratedProtoFile(GetProtoPath(service), builder.ToString());
    }

    private static IReadOnlyList<ModelShape> GetReachableShapes(
        SmithyModel model,
        ModelShape service
    )
    {
        var reachable = new Dictionary<ShapeId, ModelShape>();
        var queue = new Queue<ShapeId>();

        foreach (var operationId in service.Operations)
        {
            queue.Enqueue(operationId);
        }

        while (queue.Count > 0)
        {
            var shapeId = queue.Dequeue();
            if (!model.Shapes.TryGetValue(shapeId, out var shape) || reachable.ContainsKey(shapeId))
            {
                continue;
            }

            reachable[shapeId] = shape;

            foreach (var member in shape.Members.Values)
            {
                if (ShouldTraverse(member.Target))
                {
                    queue.Enqueue(member.Target);
                }
            }

            if (shape.Input is { } input)
            {
                queue.Enqueue(input);
            }

            if (shape.Output is { } output)
            {
                queue.Enqueue(output);
            }

            foreach (var errorId in shape.Errors)
            {
                queue.Enqueue(errorId);
            }
        }

        return reachable
            .Values.Where(shape =>
                shape.Kind is ShapeKind.Structure or ShapeKind.Enum or ShapeKind.IntEnum
            )
            .OrderBy(shape => shape.Id.ToString(), StringComparer.Ordinal)
            .ToArray();
    }

    private static bool ShouldTraverse(ShapeId shapeId)
    {
        return !string.Equals(shapeId.Namespace, SmithyPrelude.Namespace, StringComparison.Ordinal);
    }

    private static void AppendEnum(StringBuilder builder, ModelShape shape)
    {
        builder.AppendLine($"enum {shape.Id.Name} {{");
        var members = shape
            .Members.Values.OrderBy(member => member.Name, StringComparer.Ordinal)
            .ToArray();
        for (var index = 0; index < members.Length; index++)
        {
            var member = members[index];
            var numericValue =
                shape.Kind == ShapeKind.IntEnum ? GetIntEnumValue(member, index) : index;
            builder.AppendLine(
                $"  {member.Name} = {numericValue.ToString(CultureInfo.InvariantCulture)};"
            );
        }
        builder.AppendLine("}");
    }

    private static int GetIntEnumValue(MemberShape member, int defaultValue)
    {
        return member.Traits.GetValueOrDefault(SmithyPrelude.EnumValueTrait) is { } value
            ? (int)value.AsNumber()
            : defaultValue;
    }

    private static void AppendMessage(
        SmithyModel model,
        StringBuilder builder,
        ModelShape shape,
        string currentNamespace
    )
    {
        builder.AppendLine($"message {shape.Id.Name} {{");
        var members = shape
            .Members.Values.OrderBy(member => GetFieldNumber(member, shape), Comparer<int>.Default)
            .ThenBy(member => member.Name, StringComparer.Ordinal)
            .ToArray();
        ValidateFieldNumbers(members, shape);
        foreach (var member in members)
        {
            builder.AppendLine(
                $"  {FormatFieldDeclaration(model, shape, member, currentNamespace)};"
            );
        }
        builder.AppendLine("}");
    }

    private static string FormatFieldDeclaration(
        SmithyModel model,
        ModelShape shape,
        MemberShape member,
        string currentNamespace
    )
    {
        var optionalKeyword = ShouldEmitProto3Optional(model, member) ? "optional " : string.Empty;
        var fieldType = FormatFieldType(model, member.Target, currentNamespace);
        var fieldNumber = GetFieldNumber(member, shape);
        return $"{optionalKeyword}{fieldType} {member.Name} = {fieldNumber.ToString(CultureInfo.InvariantCulture)}";
    }

    private static int GetFieldNumber(MemberShape member, ModelShape shape)
    {
        if (member.Traits.GetValueOrDefault(SmithyPrelude.ProtoIndexTrait) is { } value)
        {
            return (int)value.AsNumber();
        }

        throw new SmithyException(
            $"Proto generation requires alloy.proto#protoIndex on member '{member.Id}' to preserve stable field numbers."
        );
    }

    private static void ValidateFieldNumbers(IReadOnlyList<MemberShape> members, ModelShape shape)
    {
        var fieldNumbers = new HashSet<int>();
        foreach (var member in members)
        {
            var fieldNumber = GetFieldNumber(member, shape);
            if (fieldNumber <= 0)
            {
                throw new SmithyException(
                    $"Proto generation requires a positive alloy.proto#protoIndex on member '{member.Id}'."
                );
            }

            if (!fieldNumbers.Add(fieldNumber))
            {
                throw new SmithyException(
                    $"Proto generation found duplicate alloy.proto#protoIndex value '{fieldNumber}' in shape '{shape.Id}'."
                );
            }
        }
    }

    private static bool ShouldEmitProto3Optional(SmithyModel model, MemberShape member)
    {
        if (member.IsRequired)
        {
            return false;
        }

        if (member.DefaultValue is not null)
        {
            return true;
        }

        if (
            string.Equals(
                member.Target.Namespace,
                SmithyPrelude.Namespace,
                StringComparison.Ordinal
            )
        )
        {
            return member.Target.Name
                is "String"
                    or "Boolean"
                    or "Blob"
                    or "Byte"
                    or "Short"
                    or "Integer"
                    or "Long"
                    or "Float"
                    or "Double";
        }

        var shape = model.GetShape(member.Target);
        return shape.Kind is ShapeKind.Enum or ShapeKind.IntEnum;
    }

    private static string FormatFieldType(
        SmithyModel model,
        ShapeId target,
        string currentNamespace
    )
    {
        if (string.Equals(target.Namespace, SmithyPrelude.Namespace, StringComparison.Ordinal))
        {
            return target.Name switch
            {
                "String" => "string",
                "Boolean" => "bool",
                "Blob" => "bytes",
                "Byte" or "Short" or "Integer" => "int32",
                "Long" => "int64",
                "Float" => "float",
                "Double" => "double",
                _ => throw new NotSupportedException(
                    $"Smithy prelude target '{target}' is not supported for proto generation yet."
                ),
            };
        }

        var shape = model.GetShape(target);
        return shape.Kind switch
        {
            ShapeKind.List or ShapeKind.Set =>
                $"repeated {FormatFieldType(model, shape.Members.TryGetValue("member", out var member) ? member.Target : throw new NotSupportedException($"Collection shape '{shape.Id}' is missing its member target."), currentNamespace)}",
            ShapeKind.Map =>
                $"map<{FormatFieldType(model, shape.Members["key"].Target, currentNamespace)}, {FormatFieldType(model, shape.Members["value"].Target, currentNamespace)}>",
            ShapeKind.Structure or ShapeKind.Enum or ShapeKind.IntEnum => QualifyType(
                target,
                currentNamespace
            ),
            _ => throw new NotSupportedException(
                $"Shape kind '{shape.Kind}' is not supported for proto generation yet."
            ),
        };
    }

    private static string QualifyType(ShapeId target, string currentNamespace)
    {
        return string.Equals(target.Namespace, currentNamespace, StringComparison.Ordinal)
            ? target.Name
            : $"{target.Namespace}.{target.Name}";
    }

    private static void AppendService(SmithyModel model, StringBuilder builder, ModelShape service)
    {
        builder.AppendLine($"service {service.Id.Name} {{");
        foreach (
            var operationId in service.Operations.OrderBy(
                id => id.ToString(),
                StringComparer.Ordinal
            )
        )
        {
            var operation = model.GetShape(operationId);
            builder.AppendLine(
                $"  rpc {operationId.Name} ({GetMessageName(model, operation, isInput: true)}) returns ({GetMessageName(model, operation, isInput: false)});"
            );
        }
        builder.AppendLine("}");
    }

    private static bool UsesGoogleProtobufEmpty(SmithyModel model, ModelShape service)
    {
        return service
            .Operations.Select(model.GetShape)
            .Any(operation => operation.Input is null || operation.Output is null);
    }

    private static string GetMessageName(SmithyModel model, ModelShape operation, bool isInput)
    {
        var shapeId = isInput ? operation.Input : operation.Output;
        return shapeId is { } value ? model.GetShape(value).Id.Name : "google.protobuf.Empty";
    }

    private static string GetProtoPath(ModelShape service)
    {
        return string.Join('/', service.Id.Namespace.Split('.')) + $"/{service.Id.Name}.proto";
    }

    private static string GetCSharpNamespace(ModelShape service, ProtoGenerationOptions options)
    {
        var baseNamespace = CSharp.CSharpIdentifier.Namespace(
            service.Id.Namespace,
            options.BaseNamespace
        );
        return string.IsNullOrWhiteSpace(options.CSharpNamespaceSuffix)
            ? baseNamespace
            : $"{baseNamespace}.{options.CSharpNamespaceSuffix}";
    }
}

#pragma warning restore CA1305
