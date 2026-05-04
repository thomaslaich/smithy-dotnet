# Shapes

How Smithy shapes map to C# types in generated code.

## Simple Shapes

| Smithy type | C# type |
| --- | --- |
| `blob` | `byte[]` |
| `boolean` | `bool` |
| `string` | `string` |
| `byte` | `sbyte` |
| `short` | `short` |
| `integer` | `int` |
| `long` | `long` |
| `float` | `float` |
| `double` | `double` |
| `bigInteger` | `System.Numerics.BigInteger` |
| `bigDecimal` | `decimal` |
| `timestamp` | `DateTimeOffset` |
| `document` | `NSmithy.Core.Document` |

Optional members (those without `@required`) are generated as their nullable
counterpart (e.g. `string?`, `int?`).

## String Enums (`@enum` / `enum` shape)

Smithy string enums are generated as a C# `readonly record struct` wrapping a
`string`. The known values are exposed as `public static readonly` constants on
the type.

Using a record struct rather than a `System.Enum` subclass preserves forward
compatibility: an API response can carry an unknown enum value and the client
code compiles and runs without change.

```csharp
// Smithy: enum Status { ACTIVE INACTIVE }
public readonly record struct Status(string Value)
{
    public static readonly Status Active = new("ACTIVE");
    public static readonly Status Inactive = new("INACTIVE");
}
```

## Integer Enums (`intEnum` shape)

Smithy integer enums are generated as a C# `readonly record struct` wrapping an
`int`, following the same forward-compatibility reasoning as string enums.

```csharp
// Smithy: intEnum Priority { LOW = 1, HIGH = 2 }
public readonly record struct Priority(int Value)
{
    public static readonly Priority Low = new(1);
    public static readonly Priority High = new(2);
}
```

## Structures

Structures are generated as C# `record` types with positional constructor
parameters for required members and optional (nullable) properties for
non-required members. Required members that carry a default value in the Smithy
model are generated with that default.

```csharp
// Smithy:
// structure GetWidgetInput {
//     @required @httpLabel id: String
//     filter: String
// }
public record GetWidgetInput(string Id, string? Filter = null);
```

Each generated structure implements `ISerializableShape` and
`IDeserializableShape` (see [serialization.md](serialization.md)) and carries a
`static readonly Schema` field.

## Errors

Smithy error shapes are generated as C# `Exception` subclasses. The generated
class extends a service-specific base error type, which in turn extends
`SmithyException` from `NSmithy.Core`.

```csharp
// Smithy: @error("client") structure NotFoundException { message: String }
public sealed class NotFoundException : HelloServiceException
{
    public string? Message { get; }
    // ...
}
```

The `@fault` value (`"client"` or `"server"`) and the error code (shape name)
are accessible as properties on the exception.

## Unions

Smithy unions are generated as a sealed C# class hierarchy. A `sealed` abstract
base class represents the union type; each member becomes a concrete nested
`record` subclass holding the member value.

```csharp
// Smithy: union Shape { circle: Circle, square: Square }
public abstract record Shape
{
    public sealed record CircleCase(Circle Value) : Shape;
    public sealed record SquareCase(Square Value) : Shape;
}
```

This enables exhaustiveness checks via `switch` expressions in C# 8+.

## Lists

Smithy `list` shapes are generated as `IReadOnlyList<T>` at usage sites. There
is no standalone generated class for a list shape; the element type is resolved
recursively and the list is represented inline.

## Maps

Smithy `map` shapes are generated as `IReadOnlyDictionary<TKey, TValue>`. Keys
must be `string` or a string-like type.

## Services

A Smithy service shape produces two generated files:

- `<Service>Client.g.cs` — a typed async client class with one method per
  operation.
- `<Service>Server.g.cs` — a handler interface (`I<Service>Handler`) and an
  ASP.NET Core adapter (`<Service>Server`).

See [codegen-architecture.md](codegen-architecture.md) for the generator
pipeline details.

## Namespace Mapping

Smithy namespace segments are capitalised to PascalCase and joined with `.` to
form a C# namespace. If `baseNamespace` is set in `smithy-build.json`, it is
prepended.

Examples (with empty `baseNamespace`):

| Smithy namespace | C# namespace |
| --- | --- |
| `example.hello` | `Example.Hello` |
| `com.example.widgets` | `Com.Example.Widgets` |

## Naming Conventions

- Type names follow PascalCase (`GetWidgetInput`, `NotFoundException`).
- Member names follow PascalCase (`FirstName`, `CreatedAt`).
- Smithy `camelCase` member names are converted to PascalCase in C#.
- Smithy names that conflict with C# keywords are escaped with a `@` prefix.
