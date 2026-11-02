# Modules example

A shop API whose operations are defined and implemented in separate modules, the
way a modular monolith splits a server. One project ties them into a single
`aws.protocols#restJson1` service. Each project generates only the shapes in its
own model files and uses the types the projects it references generated.

## Projects

| Project | Model | Generates |
| --- | --- | --- |
| `common` | `Money`, the `OrderNotFound` error | their types and schemas |
| `orders` | the `PlaceOrder` operation | its input, output, schema, and `IPlaceOrderHandler`; implements the handler |
| `payments` | the `CapturePayment` operation | its input, output, schema, and `ICapturePaymentHandler`; implements the handler |
| `api` | the `Shop` service listing both operations | only the service: `Shop.Schema.g.cs` and `Shop.Server.g.cs` |

`orders` and `payments` reference `common`, and `api` references the two
modules. The modules reference only `NSmithy.Core`: no ASP.NET Core, no protocol
package, and nothing about the service. They set `SmithyPublish` so that their
models reach `api`, which gets `common`'s model through them.

See [Splitting a service across projects](https://thomaslaich.github.io/smithy-dotnet/guides/distributing-contracts/#splitting-a-service-across-projects).

## Run

Run all commands in this README from `examples/modules`. First build the local
packages and examples (see the [examples README](../README.md#build)), then
start the API:

```bash
dotnet run --project api --urls http://localhost:5000
```

Place an order, handled by the Orders module:

```bash
curl -s -X POST http://localhost:5000/orders \
  -H 'Content-Type: application/json' \
  -d '{"total":{"amount":42,"currency":"CHF"}}'
# {"orderId":"order-42-CHF"}
```

Capture a payment, handled by the Payments module:

```bash
curl -s -X POST http://localhost:5000/orders/order-42-CHF/payments \
  -H 'Content-Type: application/json' \
  -d '{"amount":{"amount":42,"currency":"CHF"}}'
# {"paymentId":"payment-for-order-42-CHF"}
```

An unknown order fails with the shared `OrderNotFound` error (404), and an amount
below 1 with the modeled validation error (400).
