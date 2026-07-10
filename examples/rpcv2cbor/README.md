# NSmithy rpcv2Cbor Example

The Weather service from the [rest-json1 example](../rest-json1/), served over
`smithy.protocols#rpcv2Cbor` instead of REST. The model demonstrates resources,
pagination, typed errors, and retries (`@retryable`); the protocol swap shows
that neither the handler implementation nor the client call sites depend on the
wire format.

rpcv2Cbor is an RPC protocol: every operation is a `POST` to
`/service/Weather/operation/{OperationName}` with CBOR request and response
bodies, so the model carries no HTTP binding traits.

- `contracts`: the Smithy model, packaged as a contracts project.
- `server`: generated ASP.NET Core endpoints with a handwritten
  `IWeatherServiceHandler` implementation that supports real server-side
  pagination.
- `client`: generated typed client that pages through cities with the generated
  paginators and retries the flaky operation.

## Run

From the repository root, build and pack local packages:

```bash
just build
just pack
just refresh-examples
```

Start the server:

```bash
cd examples/rpcv2cbor
dotnet run --project server --urls http://localhost:5001
```

In another shell, run the client:

```bash
cd examples/rpcv2cbor
dotnet run --project client -- http://localhost:5001
```

The client walks the full surface: current time, paginated city listing (pages
and flattened items), city lookup, forecast, a modeled `NoSuchResource` error,
and three flaky forecast calls that succeed after transparent retries.

## On the Wire

There is nothing to explore in a browser; requests and responses are CBOR over
POST. To peek at a raw response:

```bash
curl -s -X POST http://localhost:5001/service/Weather/operation/GetCurrentTime \
  -H 'smithy-protocol: rpc-v2-cbor' \
  -H 'Content-Type: application/cbor' \
  -H 'Accept: application/cbor' \
  --data-binary '' | xxd
```
