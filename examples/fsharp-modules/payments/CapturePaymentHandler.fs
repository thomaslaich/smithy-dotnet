namespace NSmithy.Examples.FSharpModules.Payments

open System.Threading.Tasks
open Example.Shop

/// Captures payments, failing with the shared OrderNotFound error.
type CapturePaymentHandler() =
    interface ICapturePaymentHandler with
        member _.CapturePaymentAsync(input, _) =
            if not (input.OrderId.StartsWith "order-") then
                raise (OrderNotFound($"No order {input.OrderId}."))

            Task.FromResult { PaymentId = $"payment-for-{input.OrderId}" }
