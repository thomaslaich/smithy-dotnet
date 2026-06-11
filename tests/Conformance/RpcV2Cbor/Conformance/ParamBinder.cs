using System.Collections;
using System.Reflection;
using System.Text.Json.Nodes;
using NSmithy.Core;
using NSmithy.Core.Serde;

namespace RpcV2Cbor.Conformance;

/// <summary>
/// Materializes a generated input/output shape instance from the {@code params} object of a
/// Smithy protocol-test trait, using reflection over the type's primary constructor.
///
/// The generated C# code follows a strict shape:
///   * structures: a single ctor whose camelCase parameter names match the smithy member names;
///   * string enums: {@code readonly record struct} with a {@code (string Value)} ctor;
///   * int enums: standard C# {@code enum}.
/// We intentionally don't try to be "smart" — anything unsupported throws and the case has to
/// be excluded from the allowlist (or the binder taught the new pattern).
/// </summary>
internal static class ParamBinder
{
    public static object? Bind(Type targetType, JsonNode? value)
    {
        if (value is null)
            return null;

        // Nullable<T> wrappers
        var underlying = Nullable.GetUnderlyingType(targetType);
        if (underlying is not null)
            return Bind(underlying, value);

        if (targetType == typeof(string))
            return (string)value!;
        if (targetType == typeof(bool))
            return (bool)value!;
        if (targetType == typeof(sbyte))
            return (sbyte)(int)value!;
        if (targetType == typeof(int))
            return (int)value!;
        if (targetType == typeof(long))
            return (long)value!;
        if (targetType == typeof(short))
            return (short)value!;
        if (targetType == typeof(byte))
            return (byte)(int)value!;
        if (targetType == typeof(float))
            return BindFloat(value);
        if (targetType == typeof(double))
            return BindDouble(value);
        if (targetType == typeof(decimal))
            return (decimal)value!;
        if (targetType == typeof(byte[]))
        {
            var text = (string)value!;
            try
            {
                return Convert.FromBase64String(text);
            }
            catch (FormatException)
            {
                // Raw payload/blob test inputs use literal text rather than base64.
                return System.Text.Encoding.UTF8.GetBytes(text);
            }
        }

        if (targetType == typeof(DateTimeOffset))
            return BindDateTimeOffset(value);
        if (targetType == typeof(DateTime))
            return BindDateTimeOffset(value).UtcDateTime;

        if (targetType.IsEnum)
            return BindIntEnum(targetType, value);

        var schemaKind = GetSchemaKind(targetType);

        // String-enum codegen: readonly record struct with (string Value) ctor.
        if (targetType.IsValueType && schemaKind == ShapeKind.Enum)
        {
            return Activator.CreateInstance(targetType, (string)value!);
        }

        // Smithy unions are codegen'd as `abstract record class` with `FromMember(T)` static
        // factories per case. The JSON form is `{ "memberName": <inner> }`.
        if (schemaKind == ShapeKind.Union)
        {
            return BindUnion(targetType, value);
        }

        // Smithy lists/maps are codegen'd as wrapper records with a single ctor accepting
        // IEnumerable<T> / IEnumerable<KeyValuePair<TK, TV>>.
        if (schemaKind == ShapeKind.List)
        {
            return BindShapeList(targetType, value);
        }

        if (schemaKind == ShapeKind.Map)
        {
            return BindShapeMap(targetType, value);
        }

        if (
            targetType.IsGenericType
            && (
                targetType.GetGenericTypeDefinition() == typeof(IReadOnlyList<>)
                || targetType.GetGenericTypeDefinition() == typeof(List<>)
                || targetType.GetGenericTypeDefinition() == typeof(IEnumerable<>)
            )
        )
        {
            return BindList(targetType, value);
        }

        if (targetType.IsGenericType && IsMap(targetType))
        {
            return BindMap(targetType, value);
        }

        // Structure: single ctor, camelCase params match member names.
        return BindStructure(targetType, value);
    }

    private static float BindFloat(JsonNode? value)
    {
        var v = value!.AsValue();
        if (v.TryGetValue<string>(out var s))
            return float.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
        return (float)v;
    }

    private static double BindDouble(JsonNode? value)
    {
        var v = value!.AsValue();
        if (v.TryGetValue<string>(out var s))
            return double.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
        return (double)v;
    }

    private static DateTimeOffset BindDateTimeOffset(JsonNode value)
    {
        var v = value.AsValue();
        if (v.TryGetValue<double>(out var epoch))
        {
            // Smithy AST encodes timestamps as epoch seconds (possibly fractional).
            // Decompose to avoid precision loss when epoch > 2^53/1e7 (~28 years).
            var intSec = (long)epoch;
            var fracSec = epoch - intSec;
            var ticks = intSec * TimeSpan.TicksPerSecond
                + (long)(fracSec * TimeSpan.TicksPerSecond);
            return new DateTimeOffset(DateTime.UnixEpoch.AddTicks(ticks), TimeSpan.Zero);
        }
        return DateTimeOffset.Parse(
            (string)value!,
            System.Globalization.CultureInfo.InvariantCulture
        );
    }

    private static object BindUnion(Type unionType, JsonNode value)
    {
        var obj = value.AsObject();
        if (obj.Count != 1)
        {
            throw new InvalidOperationException(
                $"Union {unionType.FullName} expects a single-key object, got {obj.Count} keys."
            );
        }
        var (memberName, inner) = obj.First();
        var pascalName = char.ToUpperInvariant(memberName[0]) + memberName[1..];
        var factory =
            unionType.GetMethod("From" + pascalName, BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException(
                $"Union {unionType.FullName} has no factory From{pascalName}."
            );
        var inputType = factory.GetParameters()[0].ParameterType;
        var bound = Bind(inputType, inner);
        return factory.Invoke(null, [bound])!;
    }

    private static object BindShapeList(Type listShape, JsonNode value)
    {
        var ctor = SelectConstructor(listShape);
        var paramType = ctor.GetParameters()[0].ParameterType;
        // paramType is IEnumerable<T> (or similar). Recurse via the generic list path.
        var items = BindList(paramType, value);
        return ctor.Invoke([items]);
    }

    private static object BindShapeMap(Type mapShape, JsonNode value)
    {
        var ctor = SelectConstructor(mapShape);
        var paramType = ctor.GetParameters()[0].ParameterType;
        // paramType is IReadOnlyDictionary<TK,TV> (or similar). Recurse via the generic map path.
        var entries = BindMap(paramType, value);
        return ctor.Invoke([entries]);
    }

    private static object BindIntEnum(Type enumType, JsonNode value)
    {
        var raw = value!.AsValue();
        if (raw.TryGetValue<long>(out var n))
            return Enum.ToObject(enumType, n);
        if (raw.TryGetValue<string>(out var s))
        {
            return Enum.Parse(enumType, s, ignoreCase: true);
        }
        throw new InvalidOperationException($"Cannot bind {value} to enum {enumType}.");
    }

    private static object BindList(Type listType, JsonNode value)
    {
        var elementType = listType.GetGenericArguments()[0];
        var concrete = typeof(List<>).MakeGenericType(elementType);
        var list = (IList)Activator.CreateInstance(concrete)!;
        foreach (var item in value.AsArray())
            list.Add(Bind(elementType, item));
        return list;
    }

    private static object BindMap(Type mapType, JsonNode value)
    {
        var args = mapType.GetGenericArguments();
        var concrete = typeof(Dictionary<,>).MakeGenericType(args);
        var dict = (IDictionary)Activator.CreateInstance(concrete)!;
        foreach (var (k, v) in value.AsObject())
            dict[Bind(args[0], JsonValue.Create(k))!] = Bind(args[1], v);
        return dict;
    }

    private static bool IsMap(Type t)
    {
        var def = t.GetGenericTypeDefinition();
        return def == typeof(IReadOnlyDictionary<,>)
            || def == typeof(IDictionary<,>)
            || def == typeof(Dictionary<,>);
    }

    private static object BindStructure(Type type, JsonNode value)
    {
        var obj = value.AsObject();
        var ctor = SelectConstructor(type);
        var parameters = ctor.GetParameters();
        var args = new object?[parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
        {
            var p = parameters[i];
            // Generated ctor params are PascalCase; Smithy test fixture params are camelCase.
            var memberName = char.ToLowerInvariant(p.Name![0]) + p.Name![1..];
            if (obj.TryGetPropertyValue(memberName, out var node) && node is not null)
            {
                args[i] = Bind(p.ParameterType, node);
            }
            else if (p.HasDefaultValue)
            {
                args[i] = p.DefaultValue;
            }
            else if (p.ParameterType.IsValueType)
            {
                args[i] = Activator.CreateInstance(p.ParameterType);
            }
            else
            {
                args[i] = null;
            }
        }
        return ctor.Invoke(args);
    }

    private static ConstructorInfo SelectConstructor(Type type)
    {
        // Pick the ctor with the most parameters — the generated primary ctor.
        var ctors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        ConstructorInfo? best = null;
        foreach (var c in ctors)
        {
            if (best is null || c.GetParameters().Length > best.GetParameters().Length)
                best = c;
        }
        return best
            ?? throw new InvalidOperationException($"No public constructor on {type.FullName}.");
    }

    private static ShapeKind? GetSchemaKind(Type targetType)
    {
        // The functional schema lives on the generated companion `{Type}Schema` class.
        var schemaType = targetType.Assembly.GetType(targetType.FullName + "Schema");
        var schemaProp = schemaType?.GetProperty(
            "Schema",
            BindingFlags.Public | BindingFlags.Static
        );
        return (schemaProp?.GetValue(null) as Schema)?.Kind;
    }
}
