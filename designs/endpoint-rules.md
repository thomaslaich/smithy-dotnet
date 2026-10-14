# Endpoint Rules

How a service's modeled endpoint rules become a generated C# endpoint resolver.

## Overview

A service declares its endpoint logic with the Smithy
[rules engine](https://smithy.io/2.0/additional-specs/rules-engine/index.html):
FIPS and dual-stack variants, partitions, account-ID routing, S3 virtual-host
addressing, and so on. NSmithy compiles that logic at build time into a
generated `IEndpointResolver` for the service. Generated clients use it by
default, so a client for a rules-bearing service needs only its parameters
(for AWS, typically just a region):

```csharp
var client = new LambdaClient(new LambdaClientConfig
{
    Region = "eu-central-1",
    AuthSchemes = { new AwsSigV4AuthScheme("lambda", "eu-central-1", credentials) },
});
```

The generator works from one representation of the rules: the binary decision
diagram (BDD) of `@smithy.rules#endpointBdd`. A service that carries only the
decision tree of `@smithy.rules#endpointRuleSet` has it compiled to a BDD during
code generation. The generated resolver walks the BDD over a static node table,
testing conditions and building results with plain C# expressions; nothing is
parsed or interpreted at runtime.

## Goals

- Clients for services with endpoint rules resolve endpoints from the model
  with no hand-written resolver.
- One code path for every model, whichever rule form it ships.
- Resolution allocates nothing beyond its result, and client-level
  configuration that fixes the endpoint resolves it once.
- Trimming- and AOT-safe: no reflection, no runtime rule parsing.
- Every `@endpointTests` case of a supported service passes.
- AWS-specific functions, built-ins, and partition data stay in AWS packages;
  the core generator and runtime know only the standard library.

## Non-goals

- Account IDs derived from resolved credentials. `AWS::Auth::AccountId` is a
  configuration value.
- SigV4a signing. An endpoint that narrows auth to `sigv4a` fails auth
  selection unless a SigV4a scheme is configured.
- Endpoint discovery (`aws.api#clientEndpointDiscovery`).

## Rule input

For each service, the generator takes the `EndpointBddTrait`:

1. If the service has `@endpointBdd`, that trait is used as is.
2. Otherwise, if it has `@endpointRuleSet`, the rule set is compiled with
   `CompileBdd.compileBdd`. Services whose rule set uses AWS functions are
   compiled with `CompileBddForAws`, which applies the AWS-specific tree
   rewrites first.
3. A service with neither gets no generated resolver.

A BDD has a parameter list, a condition list, a result list, and a node table.
Each node is a triple `(condition, high, low)`. References encode the walk:

| Reference | Meaning |
| --- | --- |
| `1`, `-1` | Terminal: no rule matched. |
| `2..` | Node `ref - 1`. |
| `..-2` | Node `-ref - 1`, with the condition's outcome complemented. |
| `>= 100_000_000` | Result `ref - 100_000_000`. |

Result `0` is the implicit no-match result. Every other result is an endpoint
or an error, without conditions.

Compiled BDDs use the rules-engine 1.1 functions `coalesce`, `ite`, and
`split` regardless of the source rule set's version, so the generator supports
the full 1.1 standard library.

## Generated code

Each rules-bearing service gets `{Service}.Endpoints.Client.g.cs` with two
types.

### Parameters

`{Service}EndpointParameters` is a record with one property per rule
parameter, named in PascalCase, typed from the parameter type, and nullable
unless the parameter has a default:

```csharp
public sealed record LambdaEndpointParameters
{
    /// <summary>The AWS region used to dispatch the request.</summary>
    public string? Region { get; init; }

    public bool UseDualStack { get; init; } = false;

    public bool UseFips { get; init; } = false;

    /// <summary>Override the endpoint used to send this request.</summary>
    public string? Endpoint { get; init; }
}
```

| Rule type | C# type |
| --- | --- |
| `String` | `string` |
| `Boolean` | `bool` |
| `StringArray` | `IReadOnlyList<string>` |

Deprecated parameters are marked `[Obsolete]` with the modeled message.

### Resolver

`{Service}EndpointResolver` implements `IEndpointResolver` and exposes a
synchronous `Resolve` over the typed parameters. The node table is a static
`ReadOnlySpan<int>` over constant data, so it lives in the assembly image and
is never allocated. Conditions and results are `switch` cases over their
indexes, evaluated by a local function that shares the method's locals:

```csharp
public sealed partial class LambdaEndpointResolver : IEndpointResolver
{
    private const int Root = 2;
    private const int ResultOffset = 100_000_000;

    // (condition, high, low) per node.
    private static ReadOnlySpan<int> Nodes => [-1, 1, -1, 0, 3, 4, /* ... */];

    public SmithyEndpoint Resolve(LambdaEndpointParameters parameters)
    {
        var region = parameters.Region;
        var useFips = parameters.UseFips;
        // ...
        AwsPartition? partitionResult = null;
        EndpointUrl? url = null;

        var nodes = Nodes;
        var reference = Root;
        while (IsNode(reference))
        {
            var index = (reference > 0 ? reference : -reference) - 1;
            var outcome = Test(nodes[index * 3]) ^ (reference < 0);
            reference = outcome ? nodes[index * 3 + 1] : nodes[index * 3 + 2];
        }
        return reference >= ResultOffset
            ? Result(reference - ResultOffset)
            : throw SmithyEndpointResolutionException.NoMatch();

        bool Test(int condition) => condition switch
        {
            // isSet(Endpoint)
            0 => endpoint is not null,
            // url = parseURL(Endpoint)
            1 => (url = EndpointFunctions.ParseUrl(endpoint!)) is not null,
            // partitionResult = aws.partition(Region)
            2 => (partitionResult = AwsEndpointFunctions.Partition(region!)) is not null,
            // booleanEquals(UseFIPS, true)
            3 => useFips,
            // ...
            _ => throw new UnreachableException(),
        };

        SmithyEndpoint Result(int result) => result switch
        {
            1 => new SmithyEndpoint(new Uri($"https://lambda-fips.{region}.{partitionResult!.DnsSuffix}")),
            2 => throw new SmithyEndpointResolutionException(
                "FIPS and custom endpoint are not supported"),
            // ...
            _ => throw SmithyEndpointResolutionException.NoMatch(),
        };
    }
}
```

Each case carries its source expression as a comment, so a failing
`@endpointTests` case can be traced to the condition or result that produced
it.

The BDD's conditions share one scope. Variables bound with `assign` have
unique names after compilation, so each becomes one nullable local, assigned
in the condition that binds it and read by later conditions and results.

### Expressions

| Rules expression | Generated C# |
| --- | --- |
| Parameter reference | The parameter's local |
| `assign` variable | The variable's local |
| String literal or template | Constant or interpolated string |
| `isSet(x)` | `x is not null` |
| `not(x)` | `!x` |
| `booleanEquals(a, b)` | `a == b` |
| `stringEquals(a, b)` | `string.Equals(a, b, StringComparison.Ordinal)` |
| `getAttr(x, "a.b[0]")` | Member access, with `EndpointFunctions.Index` for list indexes |
| `coalesce(a, b, ...)` | `a ?? b ?? ...` |
| `ite(c, a, b)` | `c ? a : b` |
| Other library functions | A static call on the function's runtime class |

A condition holds when its value is non-null and not `false`, matching the
rules engine's truthiness. A condition with `assign` stores its value in the
variable's local before the test.

Arguments a function requires to be set are passed with `!`. The BDD only
reaches such a condition after the guarding `isSet` holds, as the rule set's
type checker requires.

Record-valued functions return generated-code-friendly records instead of
generic maps, so `getAttr` is member access:

| Function | Runtime type |
| --- | --- |
| `parseURL` | `EndpointUrl(Scheme, Authority, Path, NormalizedPath, IsIp)` |
| `aws.partition` | `AwsPartition(Name, DnsSuffix, DualStackDnsSuffix, SupportsFips, SupportsDualStack, ImplicitGlobalRegion)` |
| `aws.parseArn` | `AwsArn(Partition, Service, Region, AccountId, ResourceId)` |

### Results

An endpoint result becomes a `SmithyEndpoint`:

- `url` is the template's string, parsed as an absolute `Uri`.
- `headers` become `Headers`, each header with its list of values.
- `properties.authSchemes` becomes `AuthSchemes` (see
  [Auth schemes](#auth-schemes)).
- Every other property is kept in `Properties` as a `Document`.

An error result throws `SmithyEndpointResolutionException` with the rule's
message. Reaching a no-match terminal throws the same exception with a
message saying no rule matched.

## Parameter binding

The generated resolver builds `{Service}EndpointParameters` for each call.
Values come from these sources, most specific first, as the rules engine
specification orders them:

1. `@staticContextParams` on the operation: constants.
2. `@contextParam` on input members: the member's value.
3. `@operationContextParams` on the operation: a JMESPath subset over the
   input (identifiers, sub-expressions, wildcards, multi-select lists,
   flatten, and `keys`), compiled to C# member access and projections.
4. `@clientContextParams` on the service: client configuration.
5. Built-ins: client configuration.
6. The parameter's default.

Sources 4 and 5 do not vary by call. The resolver binds them once, at
construction, into a base parameters record, and each call applies the
operation's bindings with a `with` expression, dispatched on the operation id:

```csharp
private LambdaEndpointParameters Bind(SmithyEndpointParameters call) =>
    call.OperationId.Name switch
    {
        "Invoke" => clientParameters with { FunctionName = ((InvokeInput)call.Input!).FunctionName },
        _ => clientParameters,
    };
```

An operation with no static, context, or operation-context parameters
resolves the same endpoint on every call. The resolver memoizes those
endpoints per operation. When no operation in the service binds per-operation
parameters, the resolver resolves once at construction and exposes the result
as `StaticEndpoint`, so the client runtime keeps its static-endpoint fast
path. A configuration the rules reject (FIPS with a custom endpoint, say) then
fails client construction instead of the first call.

### Client configuration

The generated `{Service}ClientConfig` gets one property per client-level
parameter:

- Each `@clientContextParams` entry becomes a property named after the
  parameter in PascalCase, typed from its definition, with its documentation.
- Each built-in becomes the property its provider defines. The core provides
  `SDK::Endpoint`, bound from the existing `Endpoint` (and so from the
  endpoint constructor and `HttpClient.BaseAddress`). The AWS provider defines
  `Region`, `UseFips`, `UseDualStack`, `AccountId`, `AccountIdEndpointMode`,
  `CredentialScope`, and the S3, S3 Control, and STS built-ins.
- A parameter bound to both a client context parameter and a built-in (S3's
  `ForcePathStyle`, for one) gets a single property.

Only parameters the rule set declares produce properties. A built-in no
provider recognizes fails code generation with a message naming it.

### Client construction

A client for a rules-bearing service resolves endpoints with its generated
resolver unless `Config.EndpointResolver` is set, which replaces it. It
needs no endpoint at construction, so its config constructor is public:

```csharp
new LambdaClient(config);                 // endpoint from the rules
new LambdaClient(endpoint, config);       // rules see the endpoint as SDK::Endpoint
new LambdaClient(httpClient, config);     // likewise for HttpClient.BaseAddress
```

When the rule set declares no `SDK::Endpoint` parameter, a configured
endpoint bypasses the rules and routes every operation to that endpoint.

Clients for services without endpoint rules require an endpoint and resolve
with `StaticEndpointResolver`.

## Runtime

### Core

`NSmithy.Client` holds the endpoint types and the standard library:

```csharp
public sealed record SmithyEndpoint(
    Uri Uri,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? Headers = null,
    IReadOnlyList<SmithyEndpointAuthScheme>? AuthSchemes = null,
    IReadOnlyDictionary<string, Document>? Properties = null);

public sealed record SmithyEndpointAuthScheme(
    string SchemeId,
    IReadOnlyDictionary<string, Document> Properties);

public sealed class SmithyEndpointResolutionException : Exception;
```

`NSmithy.Client.Endpoints.EndpointFunctions` implements `parseURL`,
`substring`, `uriEncode`, `isValidHostLabel`, `split`, and list indexing
(negative indexes count from the end; out of range is null). The remaining
standard functions compile to C# operators and need no runtime support.

### AWS

`NSmithy.Aws.Endpoints.AwsEndpointFunctions` implements `aws.partition`,
`aws.parseArn`, and `aws.isVirtualHostableS3Bucket`. Partition data is a
generated C# table built from the `partitions.json` of `smithy-aws-endpoints`
at the Smithy version NSmithy builds against. `aws.partition` matches a region
exactly, then by each partition's region pattern, and falls back to the `aws`
partition.

Generated code for a service whose rules use AWS functions or built-ins
references `NSmithy.Aws`.

### Auth schemes

`properties.authSchemes` in an endpoint result is a list of records, each
with a `name` and scheme properties. The generator turns it into
`SmithyEndpoint.AuthSchemes`, mapping each name to the auth trait's shape id
the client's schemes are keyed by (`sigv4` to `aws.auth#sigv4`, `sigv4a` to
`aws.auth#sigv4a`); names without a mapping are kept as written. The record's
other fields become the scheme's `Properties`.

Auth selection narrows the operation's modeled schemes to those the endpoint
lists, as for any resolver. The selected scheme's signer reads its entry from
the resolved endpoint in the `SmithyContext`. `AwsSigV4Signer` takes
`signingName`, `signingRegion`, `disableDoubleEncoding`, and
`disableNormalizePath` from it, falling back to the service and region it was
constructed with for anything the endpoint does not set.

## Code generation structure

Endpoint generation lives in `smithy-csharp-codegen`, which depends on
`smithy-rules-engine` and `smithy-aws-endpoints`; both are part of the bundled
codegen repository used by `NSmithy.MSBuild`.

The generator core handles the BDD walk, the standard library, parameter
binding, and result construction. Two extension points, contributed through
`CSharpIntegration`, keep AWS knowledge out of it:

- **Function providers** map a rules function id to its runtime call and
  result type. The core provides the standard library; the AWS integration
  provides `aws.*`.
- **Built-in providers** map a built-in id to a client configuration
  property. The core provides `SDK::Endpoint`; the AWS integration provides
  `AWS::*`.

The AWS integration also owns the auth scheme name mapping and the choice of
`CompileBddForAws`. It is registered through the Java service loader like any
third-party integration.

A function or built-in no provider handles fails code generation, naming the
service and the id.

## Testing

- **Generator tests** in `smithy-csharp-codegen` check the generated resolver
  for small inline models: each expression form, parameter binding source,
  and result shape.
- **Runtime tests** in `NSmithy.Tests` cover `EndpointFunctions` and
  `AwsEndpointFunctions` with the vectors from the Smithy test suites, and a
  fixture model that exercises client construction, memoization, and SigV4
  overrides.
- **Conformance** in `tests/Conformance/Endpoints` runs every `@endpointTests`
  case. Its models come from Maven: `smithy-rules-engine-tests` for the
  standard library (these carry only decision trees, so every case also tests
  BDD compilation) and AWS service models from `software.amazon.api.models`
  (S3, S3 Control, STS, Lambda, DynamoDB) for the AWS functions, built-ins,
  and auth scheme properties. Cases with only `params` call `Resolve`
  directly; cases with `operationInputs` invoke the generated client against
  a recording handler and check the endpoint and headers of the sent request.
  The pass rate feeds the docs' protocol status page like the other
  conformance projects.
