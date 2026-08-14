# TypeSpec stack, wired, but not yet in the parity set

The generation pipeline works and is reproducible:

```sh
npm install
npm run import     # OpenAPI 3 -> TypeSpec, via tsp-openapi3
npm run generate   # TypeSpec -> ASP.NET Core controllers, via @typespec/http-server-csharp
```

`npm run import` reads the OpenAPI document emitted by building the NSmithy
stack, so the contract is not re-authored by hand, it is derived from the same
Smithy model as everything else.

## Why it is not benchmarked

`@typespec/http-server-csharp` is at **0.58.0-alpha.30**. As of that version its
generated server cannot serve this contract correctly, in two ways that are not
cosmetic. Both are visible in the generated source, without running it:

**1. Response header bindings are emitted into the JSON body.**

`SearchItems` binds `x-total-count` as a response header. The emitter generates a
wrapper model instead:

```csharp
public partial class NSmithyBenchmarkServiceOperationsSearchItemsResponse
{
    [JsonPropertyName("xTotalCount")] public int XTotalCount { get; set; }
    [JsonPropertyName("body")] public SearchItemsResponseContent Body { get; set; }
}
```

and the action returns `Ok(result)`. The response is therefore
`{"xTotalCount":4998,"body":{...}}` with no `x-total-count` header. Nothing in
the generated output writes a response header at all, `grep -rn 'Headers\['`
over `generated/` returns nothing.

**2. `statusCode` is serialized as a body property.**

`NSmithyBenchmarkServiceOperationsCreateOrderResponse` and `Model0` (the 404
shape) both carry a `[JsonPropertyName("statusCode")]` property, so the status
code appears as an extra field inside the response body.

## Why it was not patched into parity

Both problems are fixable by hand, wrapper types, an action filter, a custom
result. But then the benchmark measures those patches rather than what TypeSpec
generates, and the comparison stops meaning anything. The alternative, admitting
a stack that serves different bytes, is what the parity gate exists to prevent.

Excluding it is the honest option. Nothing is lost: the pipeline above is
committed, so re-checking a newer emitter is a two-command job.

## Revisiting

Re-run the two commands against a newer `@typespec/http-server-csharp`. If
`grep -rn 'Headers\[' generated/` finds header writes and the `statusCode`
properties are gone, add a `Bench.Stack.TypeSpec.csproj` compiling
`tsp-output/@typespec/http-server-csharp/generated/`, implement
`InSmithyBenchmarkServiceOperations` against `Bench.Domain`, register it in
`BenchStacks`, and run `just bench-parity`.

Note also that this emitter produces MVC controllers, which is why the suite
carries a hand-written MVC baseline, so the hosting-model cost can be separated
from the generated-code cost.
