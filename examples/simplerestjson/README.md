# NSmithy simpleRestJson Example

A Pizza Admin service built with `alloy#simpleRestJson`. The model is adapted from the
[Alloy protocol tests](https://github.com/disneystreaming/alloy) and demonstrates
unions, enums, maps, errors, client API-key auth, HTTP header binding, and
payload binding.

- `contracts`: the Smithy model, packaged as a contracts project.
- `server`: generated ASP.NET Core endpoints with a handwritten `IPizzaAdminServiceHandler` implementation.
- `client`: generated typed client that calls the server.

The server and client reference the contracts project directly. No
`smithy-build.json` is needed — NSmithy synthesizes one from the model sources
and Maven dependencies declared in the contracts project.

## Run

From the repository root, build and pack local packages:

```bash
just build
just pack
just refresh-examples
```

Start the server:

```bash
cd examples/simplerestjson
pixi shell  # not needed when using direnv
dotnet run --project server --urls http://localhost:5000
```

In another shell, run the client:

```bash
cd examples/simplerestjson
dotnet run --project client -- http://localhost:5000
```

With the server running, open in your browser:

| Route | Description |
|-------|-------------|
| [`/docs`](http://localhost:5000/docs) | smithy-docgen generated documentation |

Or call the server directly:

```bash
curl -i http://localhost:5000/health
curl -i -H 'X-Api-Key: nsmithy-demo-key' http://localhost:5000/authenticated-health
curl -i http://localhost:5000/version
curl -i http://localhost:5000/restaurant/napoli/menu
curl -i -X POST http://localhost:5000/restaurant/napoli/menu/item \
  -H 'Content-Type: application/json' \
  -d '{"food":{"pizza":{"name":"Quattro Formaggi","base":"T","toppings":["CHEESE"]}},"price":11.0}'
```

The generated client configures `HttpApiKeyAuthScheme`, which adds the
`X-Api-Key` header for operations modeled with `@httpApiKeyAuth`. The
`/authenticated-health` handler validates that header to keep the example
end-to-end.

The `/openUnions` endpoint round-trips an `OpenUnionsPayload` union. There are two variants:

**Tagged union** — the variant name is the key, and its value is the payload:

```bash
curl -i -X PUT http://localhost:5000/openUnions \
  -H 'Content-Type: application/json' \
  -d '{"tagged":{"str":"hello"}}'
```

**Discriminated union** — the discriminator field (`key`) is inlined into the object alongside the payload fields:

```bash
curl -i -X PUT http://localhost:5000/openUnions \
  -H 'Content-Type: application/json' \
  -d '{"discriminated":{"key":"smol","content":"hello"}}'
```

Both return the body echoed back with `200 OK`.
