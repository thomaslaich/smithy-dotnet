# Shape Mapping

How Smithy shapes map to C# types in generated NSmithy code.

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
model are generated with that default. Structures are plain model types: they
do not implement serialization interfaces and do not contain wire-format
callbacks.

```csharp
// Smithy:
// structure GetWidgetInput {
//     @required @httpLabel id: String
//     filter: String
// }
public record GetWidgetInput(string Id, string? Filter = null);
```

Schema metadata is emitted separately, usually in a generated companion type:

```csharp
public static class GetWidgetInputSchemas
{
    public static readonly Schema<GetWidgetInput> Schema = ...;
}
```

The schema contains Smithy member names, traits, target schemas, typed member
accessors, and builder hooks used by codecs during deserialization. Keeping the
schema separate lets the POCO remain useful as an ordinary C# type while still
giving protocols and codecs full Smithy metadata.

## Errors

Smithy error shapes are generated as `sealed partial` classes that extend
`System.Exception` directly:

```csharp
// Smithy: @error("client") structure NotFoundException { message: String }
public sealed partial class NotFoundException : System.Exception
{
    public NotFoundException(string? message = null /* , modeled members */)
        : base(message) { /* ... */ }

    // modeled members surface as read-only properties
}
```

Modeled members become constructor parameters and read-only properties. The
protocol layer maps a wire error to the right exception type using the
operation's error discriminator (see [serialization.md](serialization.md)); the
generated exception exposes its modeled members but no separate fault/code
properties.

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

The union schema is emitted separately and describes each union case, its Smithy
member name, traits, target schema, case matcher, and case constructor.

## Lists

Smithy `list` shapes are generated as `IReadOnlyList<T>` at usage sites. There
is no standalone generated class for a list shape; the element type is resolved
recursively and the list is represented inline. The generated schema still
contains the list shape id, traits, and member target schema.

## Maps

Smithy `map` shapes are generated as `IReadOnlyDictionary<TKey, TValue>`. Keys
must be `string` or a string-like type. The generated schema contains the map
shape id, traits, key schema, and value schema.

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
