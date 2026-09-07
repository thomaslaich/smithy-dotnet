---
title: Smithy and TypeSpec
description: How Smithy and TypeSpec compare, and why NSmithy builds on Smithy.
---

[TypeSpec](https://typespec.io/) is a natural choice to consider for .NET teams.
It is built by Microsoft and has C# client and ASP.NET Core server generators.
Given that backing and investment in .NET tooling, you might reasonably start
there when choosing a language for your API contracts.

Both TypeSpec and Smithy address the authoring problem discussed in our
[introduction](/smithy-dotnet/getting-started/introduction/): writing and reviewing
an API contract before implementing a service, without maintaining a large
OpenAPI JSON or YAML document by hand.

This page aims to give an honest comparison from the perspective of the NSmithy
project. It includes our preferences as well as concrete examples of what each
tool generates. We explain where TypeSpec is a good fit and why we believe
NSmithy is a compelling choice for .NET services: Smithy's explicit service
model, protocol-independent handlers, and code generation integrated into the
normal .NET build. Where we prefer a design, we explain the tradeoff so you can
decide whether it matters for your project.

## Why we prefer Smithy's service model

Smithy gives [services, resources, and operations](https://smithy.io/2.0/spec/service-types.html)
explicit roles in the model. Each operation identifies its input, output, and
modeled errors. You can describe what a service does before choosing HTTP routes,
status codes, or a serialization format.

Here is a complete contract before adding a protocol:

```smithy
$version: "2"
namespace example.library

service Library {
    version: "1"
    operations: [GetBook]
}

@readonly
operation GetBook {
    input: GetBookInput
    output: GetBookOutput
    errors: [BookNotFound]
}

@input
structure GetBookInput {
    @required
    id: String
}

@output
structure GetBookOutput {
    @required
    id: String
    @required
    title: String
}

@error("client")
structure BookNotFound {
    message: String
}
```

The operation and its error are meaningful without an HTTP method or status code.
To expose it using REST/JSON, add these bindings to the same model (and make the
Alloy traits available as a model dependency):

```smithy
apply Library @alloy#simpleRestJson
apply GetBook @http(method: "GET", uri: "/books/{id}", code: 200)
apply GetBookInput$id @httpLabel
apply BookNotFound @httpError(404)
```

For RPC v2 CBOR, add the `smithy-protocol-traits` model dependency and
select this protocol instead:

```smithy
apply Library @smithy.protocols#rpcv2Cbor
```

That protocol defines the operation's wire representation without requiring the
REST bindings. The input, output, and modeled error stay the same. The .NET
server also needs the `NSmithy.Protocols.RpcV2Cbor` package for this binding.

Smithy makes that relationship explicit through
[protocol traits](https://smithy.io/2.0/spec/protocol-traits.html). A service can
declare multiple protocols, each defining how to interpret the model for the
wire. We find this a clean way to keep the service contract at the center of API
design while making transport decisions explicit.

## How TypeSpec approaches this

TypeSpec also separates core models and operations from protocol libraries. Its
[emitter architecture](https://typespec.io/docs/extending-typespec/emitters-basics/)
supports multiple protocols and output formats, including OpenAPI, JSON Schema,
and Protobuf. Its [HTTP library](https://typespec.io/docs/libraries/http/operations/)
adds HTTP semantics to operations through decorators.

The equivalent HTTP API in TypeSpec is more compact:

```typespec
import "@typespec/http";
using TypeSpec.Http;

@service
namespace Library;

model Book {
  id: string;
  title: string;
}

@error
model BookNotFound {
  @statusCode statusCode: 404;
  message: string;
}

@route("/books")
interface Books {
  @get @route("/{id}")
  getBook(@path id: string): Book | BookNotFound;
}
```

Here the parameter list describes the input and the return union includes the
success and error models. Smithy names the input and output structures and lists
errors separately. We prefer those explicit roles when a contract is shared by
multiple generators, even though they take more lines to write.

TypeSpec can also separate the HTTP decorators from the core definition. The
examples illustrate the service vocabulary we prefer in Smithy; both languages
allow a contract to be modeled independently of HTTP.

In either ecosystem, reusing a model across protocols depends on compatible
bindings and generator support. For NSmithy, the
[Protocol Status](/smithy-dotnet/protocols/status/) page documents what is supported.

## Governance and evolution

Both ecosystems provide ways to enforce API conventions. TypeSpec libraries can
supply [validation hooks and linter rules](https://typespec.io/docs/extending-typespec/linters/),
while Smithy provides [model validators](https://smithy.io/2.0/guides/model-linters.html).

Their evolution tools address different tasks. TypeSpec's
[versioning library](https://typespec.io/docs/libraries/versioning/reference/)
lets you annotate when elements were added, removed, renamed, or changed, and
emit different API versions from a definition. Smithy's
[Diff tool](https://smithy.io/2.0/guides/evolving-models.html#using-smithy-diff)
compares two models for backward-compatibility issues. That is useful when a team
wants CI to check a proposed contract against its previously published version.

Declaring API versions and checking compatibility are separate needs. Decide how
your team will handle both, whichever language you choose.

## From contract to server code

The design choice matters most in the code you maintain. We want handlers to
accept modeled inputs, return modeled outputs, and throw modeled errors while
NSmithy handles their protocol representation. The following implementations
show where that boundary sits in each generator.

### A TypeSpec MVC server

Save the TypeSpec example above as `main.tsp`. With the compiler, HTTP library,
and C# server emitter installed, this `tspconfig.yaml` selects the output:

```yaml
emit:
  - "@typespec/http-server-csharp"
options:
  "@typespec/http-server-csharp":
    emitter-output-dir: "{project-root}/server"
```

Run generation with:

```sh
npx tsp compile .
```

For this example, the emitter generates the following business logic interface
(omitting generated imports):

```csharp
public interface IBooks
{
    Task<Book> GetBookAsync(string id);
}
```

The generated MVC controller calls that interface. Its action looks like this:

```csharp
[HttpGet]
[Route("/books/{id}")]
[ProducesResponseType((int)HttpStatusCode.OK, Type = typeof(Book))]
public virtual async Task<IActionResult> GetBook(string id)
{
    var result = await BooksImpl.GetBookAsync(id);
    return Ok(result);
}
```

You implement the interface in your own file:

```csharp
using Library;

public sealed class Books : IBooks
{
    public Task<Book> GetBookAsync(string id)
    {
        if (id != "smithy")
            throw new BookNotFound("Book not found.");

        return Task.FromResult(new Book
        {
            Id = id,
            Title = "Designing APIs with Smithy"
        });
    }
}
```

In an ASP.NET Core web project containing the generated files, register the
implementation, MVC, and the generated exception filter:

```csharp
using Library;
using TypeSpec.Helpers;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<IBooks, Books>();
builder.Services.AddControllers(options =>
    options.Filters.Add<HttpServiceExceptionFilter>());

var app = builder.Build();
app.MapControllers();
app.Run();
```

The filter translates the generated `BookNotFound` exception into the modeled
404 response. These snippets were checked against TypeSpec compiler 1.15.0 and
`@typespec/http-server-csharp` 0.58.0-alpha.31. The emitter can also scaffold a
project and mock implementations; see its
[project documentation](https://typespec.io/docs/emitters/servers/http-server-csharp/project/).

### Generation and source control

In this workflow, `tsp compile` generates the C# files, then `dotnet build`
compiles the server. The generated project does not automatically invoke
TypeSpec. You can automate generation in a build script, CI job, or custom MSBuild
target. Checking generated code into source control is a team choice; it is not
a TypeSpec requirement.

### An NSmithy handler

NSmithy supplies the MSBuild integration: editing the Smithy contract and running
`dotnet build` regenerates the C# code. For the `Library` service above, the
application registers its handler and maps the generated minimal API routes:

```csharp
using Example.Library;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddLibraryServiceHandler<LibraryHandler>();

var app = builder.Build();
app.MapLibraryService();
app.Run();

internal sealed class LibraryHandler : ILibraryServiceHandler
{
    public Task<GetBookOutput> GetBookAsync(
        GetBookInput input,
        CancellationToken cancellationToken = default)
    {
        if (input.Id != "smithy")
            throw new BookNotFound("Book not found.");

        return Task.FromResult(new GetBookOutput(
            input.Id, "Designing APIs with Smithy"));
    }
}
```

This assumes an NSmithy server project configured for `example.library#Library`
and the REST/JSON binding above. The
[Quick Start](/smithy-dotnet/getting-started/quick-start/) shows the project setup.
NSmithy generates the routing adapter and serialization code, while your handler
works with the operation's input and output types. Contracts can also be consumed
as [versioned dependencies](/smithy-dotnet/guides/distributing-contracts/).

NSmithy generates Minimal API endpoints, avoiding the additional MVC controller
pipeline. This provides a lightweight hosting foundation while keeping protocol
handling separate from application code. Microsoft identifies
[reduced overhead compared to controllers](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/apis?view=aspnetcore-10.0)
as an advantage of Minimal APIs. Comparing the overall performance of these two
generated servers would require equivalent benchmarks.

The handler's contract is `GetBookInput` to `GetBookOutput`, with a
`BookNotFound` exception for the modeled failure. Switching this service to RPC
v2 CBOR changes the protocol adapter, while this handler implementation stays
the same. That separation is the main reason we prefer this approach.

TypeSpec's `IBooks` also keeps MVC controller types out of the business logic.
In the HTTP emitter tested here, however, `BookNotFound` derives from
`HttpServiceException` and carries the 404 status. NSmithy's generated error
derives from `Exception`; the protocol layer determines its wire representation.
This is a concrete distinction between these generators, rather than a limit
on what the TypeSpec language could support.

## Language coverage

Smithy's breadth of direct generators is another reason to consider it for an
organization using several languages. Its
[generator directory](https://github.com/smithy-lang/awesome-smithy#code-generators)
includes Java, TypeScript, Python, Go, Rust, Kotlin, Swift, Ruby, and community
projects for Scala, C#, and BEAM languages. TypeSpec's documented
[HTTP client emitters](https://typespec.io/docs/emitters/clients/introduction/)
cover JavaScript, Python, Java, and C#.

That gives Smithy a broader selection of direct language generators in these
catalogs. TypeSpec can also reach other languages through generated OpenAPI or
Protobuf and their downstream tools. For either route, check the specific
protocol, modeled features, client or server support, and generator maturity;
being listed does not guarantee interoperability for every service.

## Choosing for your project

TypeSpec is worth evaluating if its emitters fit your API and you want to use
its OpenAPI, schema, or code generation ecosystem. An existing TypeSpec workflow
does not need replacing simply to gain readable contracts or C# generation.

Smithy with NSmithy is a good fit when you want to design around explicit service
operations, keep protocol bindings separate from their inputs, outputs, and
errors, and share that contract across services and languages. NSmithy brings
that approach into the normal .NET build. Try the
[Quick Start](/smithy-dotnet/getting-started/quick-start/) with a representative
operation, then check the generated API and protocol support against your needs.
