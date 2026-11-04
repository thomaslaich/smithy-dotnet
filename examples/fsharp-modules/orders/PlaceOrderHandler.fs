namespace NSmithy.Examples.FSharpModules.Orders

open System.Threading.Tasks
open Example.Shop

/// Places orders. Knows nothing about the service that serves this operation.
type PlaceOrderHandler() =
    interface IPlaceOrderHandler with
        member _.PlaceOrderAsync(input, _) =
            Task.FromResult { OrderId = $"order-{input.Total.Amount}-{input.Total.Currency}" }
