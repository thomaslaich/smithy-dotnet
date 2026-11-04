# F# modules example

The [modules example](../modules/) in F#: a shop API whose operations are
defined and implemented in separate modules, tied into one
`aws.protocols#restJson1` service. Each project generates only the shapes in its
own model files and uses the types the projects it references generated.

| Project | Model | Generates |
| --- | --- | --- |
| `common` | `Money`, the `OrderNotFound` error | their types and schemas |
| `orders` | the `PlaceOrder` operation | its input, output, schema, and `IPlaceOrderHandler`; implements the handler |
| `payments` | the `CapturePayment` operation | its input, output, schema, and `ICapturePaymentHandler`; implements the handler |
| `api` | the `Shop` service listing both operations | only the service |

## Run

From `examples/fsharp-modules`, after building the local packages and examples
(see the [examples README](../README.md#build)):

```bash
dotnet run --project api --urls http://localhost:5000
```

The requests in the [modules example](../modules/README.md#run) work the same.
