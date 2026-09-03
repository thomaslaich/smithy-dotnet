# NSmithy rpcv2Cbor Example

The Weather service from the [restJson1 unary example](../../restjson1/unary/), served over
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
cd examples/rpcv2cbor/unary
dotnet run --project server
```

In another shell, run the client:

```bash
cd examples/rpcv2cbor/unary
dotnet run --project client -- http://localhost:5001
```

The client walks the full surface: current time, paginated city listing (pages
and flattened items), city lookup, forecast, a modeled `NoSuchResource` error,
and three flaky forecast calls that succeed after transparent retries.

## On the Wire

There is nothing to explore in a browser; requests and responses are CBOR over
POST. Pass `--debug` to the client to log every request and response with a hex
dump of the CBOR bytes, using the client runtime's built-in `DebugInterceptor`:

```bash
dotnet run --project client -- http://localhost:5001 --debug
```

```
[Weather.ListCities] request (attempt 1): POST http://localhost:5001/service/Weather/operation/ListCities
  Smithy-Protocol: rpc-v2-cbor
  Accept: application/cbor
  content-type: application/cbor
  body: 12 bytes
    00000000  bf 68 70 61 67 65 53 69 7a 65 03 ff              .hpageSize..
[Weather.ListCities] response (attempt 1): 200 OK
  Smithy-Protocol: rpc-v2-cbor
  Content-Type: application/cbor
  body: 106 bytes
    00000000  bf 69 6e 65 78 74 54 6f 6b 65 6e 63 4c 41 58 65  .inextTokencLAXe
    00000010  69 74 65 6d 73 83 bf 66 63 69 74 79 49 64 63 53  items..fcityIdcS
    00000020  45 41 64 6e 61 6d 65 67 53 65 61 74 74 6c 65 ff  EAdnamegSeattle.
    ...
```

Retries are visible as separate attempts: the flaky forecast calls log the
failed 500 responses that precede each success.

To peek at a raw response without the client:

```bash
curl -s -X POST http://localhost:5001/service/Weather/operation/GetCurrentTime \
  -H 'smithy-protocol: rpc-v2-cbor' \
  -H 'Content-Type: application/cbor' \
  -H 'Accept: application/cbor' \
  --data-binary '' | xxd
```
