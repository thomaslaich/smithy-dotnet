---
title: Extending NSmithy
description: Add protocols, service generators, and endpoint rule functions to NSmithy code generation through C# integrations, and the runtime types they ship with.
---

NSmithy is extensible without changes to NSmithy itself. A package of your own
can add a wire protocol, replace the generated client and server for a
service that is not request/response, such as one that sends messages over a
broker, or add functions and built-ins to endpoint rules. NSmithy's built-in
protocols and its AWS endpoint support use the same API.

Such an extension is a **C# integration**: a JAR on the codegen classpath that
NSmithy's generator discovers, usually paired with a NuGet package holding the
runtime types the generated code calls. Projects opt in by adding both as
dependencies.

Smaller extensions need no code generation. Generated clients take custom
[auth schemes](/smithy-dotnet/guides/client-configuration/authentication/),
[interceptors](/smithy-dotnet/guides/client-configuration/interceptors/), and
[transports](/smithy-dotnet/guides/client-configuration/transport/) at runtime.

:::caution[Unstable API]
`CSharpIntegration` and the types it receives are `@SmithyUnstableApi`. Build an
integration against the NSmithy version it runs with.
:::

## Integrations

`CSharpIntegration` extends Smithy's `SmithyIntegration`, so an integration gets
Smithy's hooks (`preprocessModel`, `decorateSymbolProvider`, `interceptors`,
`customize`) and NSmithy's own. Three of them shape a service's generated surface:

| Hook | Use | NSmithy still generates |
| --- | --- | --- |
| `protocols()` | A request/response protocol over HTTP | Client, server, DI registration, endpoint resolution, shapes |
| `serviceGenerator(context, service)` | A service that is not request/response, such as messaging | Shapes, schemas, and operations |
| `additionalShapes(model, settings, service)` | Shapes the service does not reach through its operations, such as events it publishes | Everything, plus these shapes and the shapes they reference |

Four more extend generated endpoint resolvers; see
[Extending endpoint rules](#extending-endpoint-rules).

Integrations run in the order of their `name()`, `runBefore()`, and
`runAfter()`.

Build against the codegen artifact, which the Smithy CLI provides at
generation time:

```kotlin title="build.gradle.kts"
dependencies {
    compileOnly("io.github.thomaslaich.nsmithy:smithy-csharp-codegen:NSMITHY_VERSION")
}
```

Register the integration for the Java `ServiceLoader`:

```text title="src/main/resources/META-INF/services/io.github.thomaslaich.nsmithy.csharp.codegen.integrations.CSharpIntegration"
com.acme.nsmithy.MsgPackIntegration
```

## Adding a protocol

A protocol has three parts: a protocol trait, a runtime type, and an integration
that connects them.

### Protocol trait

Define the trait in a Smithy model inside the integration's JAR, so services
that reference the JAR can apply it:

```smithy title="src/main/resources/META-INF/smithy/msgpack.smithy"
$version: "2"
namespace acme.protocols

@trait(selector: "service")
@protocolDefinition
structure msgpack {}
```

List the file in `META-INF/smithy/manifest`. Traits the protocol needs on
members, and constraints it places on the model, belong in the same JAR as
Smithy validators registered under
`META-INF/services/software.amazon.smithy.model.validation.Validator`.

### Runtime type

The runtime type implements `IProtocol` from `NSmithy.Http` and has a public
parameterless constructor, which generated code calls:

| Type | Responsibility |
| --- | --- |
| `IProtocol` | Binds to a service through `ForService(ServiceSchema)`. Optionally sets `HttpVersionPreference`. |
| `IServiceProtocol` | Hands out a per-operation protocol for the client and the server side. Reports server routes through `GetServerRouting`. |
| `IClientOperationProtocol<TInput, TOutput>` | Serializes requests, deserializes responses, and recognizes and deserializes errors. |
| `IServerOperationProtocol<TInput, TOutput>` | Deserializes requests, validates input, and serializes responses and modeled errors. |

Protocols serialize from schemas, so they need no generated serializers. The
codecs in `NSmithy.Codecs.*` cover JSON, CBOR, XML, and Protobuf.

`GetServerRouting` returns one of:

- `ServerRouting.ByRoute`: one route per operation, matched by method and path,
  as in REST protocols.
- `ServerRouting.ByDispatch`: one route for the service, and a function that
  names the operation from the request, as in RPC protocols.
- `ServerRouting.None`: the protocol is client-only. This is the default.

Ship the runtime type in a NuGet package.

### Integration

Return a `CSharpProtocol` from `protocols()`:

```java title="MsgPackIntegration.java"
public final class MsgPackIntegration implements CSharpIntegration {
  @Override
  public String name() {
    return "msgpack";
  }

  @Override
  public List<CSharpProtocol> protocols() {
    return List.of(
        new CSharpProtocol(
            ShapeId.from("acme.protocols#msgpack"),
            "MsgPack",
            CSharpProtocol.runtimeType("Acme.Protocols.MsgPackProtocol"),
            false, // eventStreams
            true)); // server
  }
}
```

| Field | Meaning |
| --- | --- |
| `trait` | The protocol trait. A service that carries it gets the protocol. |
| `name` | A C# identifier used in generated names, such as the member of the server's protocols enum. |
| `runtimeType` | The full name of the `IProtocol` implementation. |
| `eventStreams` | Whether the runtime type binds event-stream operations. |
| `server` | Whether the runtime type serves requests. |

Built-in protocols register the same way and come first. Two registrations of
one trait fail code generation. When a service has several protocols, the
client defaults to the first, or to the `defaultProtocol`
[setting](/smithy-dotnet/reference/msbuild/).

## Adding a service generator

A protocol on another transport, or with exchanges that are not one request and
one response, replaces the generated client and server with its own:

```java
@Override
public Optional<CSharpServiceGenerator> serviceGenerator(
    GenerationContext context, ServiceShape service) {
  if (!service.hasTrait(KafkaTrait.ID)) {
    return Optional.empty();
  }
  return Optional.of(new KafkaServiceGenerator());
}
```

`CSharpServiceGenerator.generate(context, service)` writes files through
`context.writerDelegator()`. Return a generator only for services your
integration owns: two integrations that both return one for a service fail code
generation.

## Adding shapes to a service

A service generator often needs types the service's operations never reference,
such as the events it publishes. Return them from `additionalShapes`:

```java
@Override
public Set<ShapeId> additionalShapes(
    Model model, CSharpSettings settings, ServiceShape service) {
  return service.getTrait(PublishesTrait.class)
      .map(PublishesTrait::getChannels)
      .orElse(Set.of());
}
```

NSmithy generates each shape with the shapes it references, alongside the
service and any configured `closure`. The hook receives the model as assembled,
before codegen transforms. A shape the model does not contain fails code
generation.

## Extending endpoint rules

Generated [endpoint resolvers](/smithy-dotnet/guides/client-configuration/endpoint-rules/)
implement the rules engine's standard library. Rules that use other functions or
built-ins need an integration that maps them to C#:

| Hook | Supplies |
| --- | --- |
| `endpointFunctions()` | A rules function id and the static C# method generated resolvers call for it |
| `endpointBuiltIns()` | A built-in id and the `{Service}ClientConfig` property that supplies it |
| `endpointAuthSchemeIds()` | Auth scheme names used in endpoint `authSchemes`, mapped to the auth trait shape ids client auth schemes are keyed by |
| `endpointDefaultAuthSchemes(service)` | Auth scheme properties for endpoints whose rules set no `authSchemes`, such as a SigV4 signing name |

NSmithy's AWS support is the built-in `AwsEndpointsIntegration`, which registers
`aws.partition`, `AWS::Region`, and the other AWS functions and built-ins
through these hooks.

```java title="AcmeEndpointsIntegration.java"
@Override
public List<EndpointFunction> endpointFunctions() {
  Symbol functions =
      Symbol.builder().name("AcmeEndpointFunctions").namespace("Acme.Endpoints", ".").build();
  return List.of(
      new EndpointFunction(
          "acme.cellHost", functions, "CellHost", EndpointValueType.STRING, true)); // nullable
}

@Override
public List<EndpointBuiltIn> endpointBuiltIns() {
  return List.of(new EndpointBuiltIn("Acme::Cell", "Cell", "The cell requests are routed to."));
}
```

```csharp title="AcmeEndpointFunctions.cs"
namespace Acme.Endpoints;

public static class AcmeEndpointFunctions
{
    public static string? CellHost(string cell) => cell.Length == 0 ? null : $"{cell}.acme.example";
}
```

The method takes the function's arguments in order. A `nullable` function
returns null where the rules expect an empty value. Smithy validates rule sets
before code generation, so the same JAR also declares the functions and
built-ins to the rules engine through Smithy's `EndpointRuleSetExtension`, as
`smithy-aws-endpoints` does for AWS.

## Using an integration

Add the integration's JAR to `maven.dependencies` and its runtime package to the
project:

```json title="smithy-build.json"
{
  "version": "1.0",
  "maven": {
    "dependencies": ["com.acme:nsmithy-msgpack:1.0.0"],
    "repositories": [{ "url": "https://maven.pkg.github.com/acme/nsmithy-msgpack" }]
  }
}
```

```xml title="MyService.Client.csproj"
<ItemGroup>
  <PackageReference Include="Acme.Protocols.MsgPack" Version="1.0.0" />
</ItemGroup>
```

NSmithy merges `maven.dependencies` into the effective build file; see
[Maven dependencies](/smithy-dotnet/reference/maven-dependencies/) for how they
resolve. A NuGet package can add the dependency for its users through a
`SmithyContractsBuildFile` fragment.

The [codegen design doc](https://github.com/thomaslaich/smithy-dotnet/blob/main/designs/codegen-architecture.md#integrations)
describes how the generator orders protocols and lists the built-in ones.
