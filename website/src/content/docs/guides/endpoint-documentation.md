---
title: Endpoint Documentation
description: Serve an interactive OpenAPI explorer and generated Sphinx documentation alongside your ASP.NET Core server.
---

NSmithy can generate two documentation UIs from your Smithy model and serve them
alongside your ASP.NET Core application:

- **Scalar** — an interactive OpenAPI explorer at `/openapi`, backed by a
  `openapi.json` generated from the model by
  [smithy-openapi](https://smithy.io/2.0/guides/converting-to-openapi.html).
  Available for AWS restJson1 services.
- **Sphinx HTML** — auto-generated reference documentation at `/docs`, produced
  by [smithy-docgen](https://github.com/smithy-lang/smithy-docgen) and compiled
  to HTML automatically at build time. Available for all services.

Both are opt-in MSBuild features. Their output is copied into `wwwroot/`, where
ASP.NET Core's static file middleware can serve it.

<figure>
  <img src="/smithy-dotnet/screenshots/scalar-ui.png" alt="Scalar interactive API explorer showing the GetForecast endpoint" style="border-radius: 0.5rem; border: 1px solid var(--sl-color-gray-5);" />
  <figcaption>Scalar interactive API explorer at <code>/openapi</code></figcaption>
</figure>

<figure>
  <img src="/smithy-dotnet/screenshots/smithy-docs.png" alt="smithy-docgen generated Sphinx HTML showing the GetForecast operation" style="border-radius: 0.5rem; border: 1px solid var(--sl-color-gray-5);" />
  <figcaption>smithy-docgen generated reference documentation at <code>/docs</code></figcaption>
</figure>

## Install the Package

Add `NSmithy.Server.AspNetCore.Docs` to your server project:

```xml
<PackageReference Include="NSmithy.Server.AspNetCore.Docs" Version="0.6.0" />
```

This package provides the `MapSmithyOpenApi()` and `MapSmithyDocs()` extension
methods.

## Enable the Generators

Set the MSBuild properties you need in your server `.csproj`:

```xml
<PropertyGroup>
  <!-- Generate Sphinx HTML documentation (all services) -->
  <SmithyGenerateDocs>true</SmithyGenerateDocs>

  <!-- Generate openapi.json for AWS restJson1 services -->
  <SmithyOpenApiProtocol>aws.protocols#restJson1</SmithyOpenApiProtocol>
</PropertyGroup>
```

Both properties are `false`/empty by default.

:::note[Protocol support for OpenAPI]
`SmithyOpenApiProtocol` requires a registered Smithy OpenAPI protocol converter.
Currently only `aws.protocols#restJson1` is supported by smithy-openapi.
`alloy#simpleRestJson` does not have a converter — omit `SmithyOpenApiProtocol`
for alloy-based services and use only `SmithyGenerateDocs`.
:::

## Register the Endpoints

Map the generated documentation endpoints in `Program.cs`:

```csharp
using NSmithy.Server.AspNetCore.Docs;

var app = builder.Build();
app.MapSmithyOpenApi(); // mounts Scalar at /openapi (requires SmithyOpenApiProtocol)
app.MapSmithyDocs();    // serves Sphinx HTML at /docs (requires SmithyGenerateDocs)
app.MapMyServiceHttp();
app.Run();
```

`MapSmithyOpenApi()` enables static file serving and mounts Scalar at
`/openapi`. The UI reads `/openapi.json`, which is generated from the model and
copied to `wwwroot/openapi.json` at build time.

`MapSmithyDocs()` enables static file serving and redirects `/docs` to
`/docs/index.html`. smithy-docgen builds the Sphinx HTML during `dotnet build`;
MSBuild then copies it to `wwwroot/docs/`.

## What Gets Generated

On `dotnet build`, NSmithy:

1. Runs `smithy build` with the `docgen` plugin (and `openapi` plugin if
   `SmithyOpenApiProtocol` is set).
2. smithy-docgen bootstraps a self-contained Python venv with Sphinx, builds the
   HTML, and writes it to `obj/<config>/<tfm>/Smithy/source/docgen/build/html/`.
3. MSBuild copies the HTML to `wwwroot/docs/` and the OpenAPI spec to
   `wwwroot/openapi.json`.

Python 3.11 or newer must be available on the host. A system Python installation
is sufficient. If you use [pixi](https://pixi.sh) or
[devenv](https://devenv.sh), declare Python as a dependency there.

Add `wwwroot/` to your `.gitignore` since the output is always regenerated at
build time:

```txt
wwwroot/
```

## Model Requirements for Docs Generation

smithy-docgen validates that input and output structures follow the Smithy best
practices before generating documentation. Specifically, structures used as
operation inputs or outputs must carry the `@input` or `@output` trait, and the
same structure must not serve as both the input and output of an operation.

Use the inline `input :=` / `output :=` syntax (which applies these traits
automatically) or annotate named structures explicitly:

```smithy
// ✅ inline syntax — @input and @output are applied automatically
operation Echo {
    input := { @required message: String }
    output := { @required message: String }
}

// ✅ named structures with explicit traits
@input  structure EchoInput  { @required message: String }
@output structure EchoOutput { @required message: String }

// ❌ shared structure — smithy-docgen will refuse this
structure EchoPayload { @required message: String }
operation Echo {
    input: EchoPayload
    output: EchoPayload  // same shape used for both roles
}
```
