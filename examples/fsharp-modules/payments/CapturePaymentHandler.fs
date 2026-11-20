namespace NSmithy.Examples.FSharpModules.Payments

open System.Threading.Tasks
open Example.Shop

/// Captures payments, failing with the shared OrderNotFound error.
type CapturePaymentHandler() =
    interface ICapturePaymentHandler with
        member _.CapturePaymentAsync(input, _) =
            if input.OrderId.StartsWith "order-" then
                Task.FromResult(Ok { PaymentId = $"payment-for-{input.OrderId}" })
            else
                Task.FromResult(Error(CapturePaymentError.OrderNotFound(OrderNotFound($"No order {input.OrderId}."))))
