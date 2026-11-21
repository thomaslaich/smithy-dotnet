module NSmithy.FSharp.Tests.TypeTests

open Example.Kitchen
open Xunit

[<Fact>]
let ``A string enum maps a value it does not define to Unknown`` () =
    Assert.Equal(Ingredient.Cheese, Ingredient.FromValue "cheese")
    Assert.Equal(Ingredient.Unknown "pineapple", Ingredient.FromValue "pineapple")
    Assert.Equal("secret", Ingredient.SecretSauce.Value)

[<Fact>]
let ``Members whose names clash with generated or inherited names take a suffix`` () =
    let receipt = Receipt.UnknownCase "a case named unknown"
    Assert.NotEqual(Receipt.Pending "a case named unknown", receipt)

let private outOfStock: OutOfStock =
    { Message = Some "gone"
      Item = Ingredient.Bacon
      RestockDays = 5
      Data = Some "d" }

[<Fact>]
let ``An error is a value-equal record`` () =
    Assert.Equal(outOfStock, { outOfStock with Data = Some "d" })
    Assert.NotEqual(outOfStock, { outOfStock with Data = None })

[<Fact>]
let ``An error's exception carries the record and its message`` () =
    let error = Exceptions.OutOfStock outOfStock
    Assert.Equal(outOfStock, error.Error)
    Assert.Equal("gone", error.Message)

[<Fact>]
let ``A retryable error's exception implements ISmithyRetryableError`` () =
    let error: exn = Exceptions.Throttled { Message = "slow down" }

    match box error with
    | :? NSmithy.Core.ISmithyRetryableError as retryable -> Assert.True(retryable.IsThrottlingError)
    | _ -> Assert.Fail "Throttled is not retryable"

[<Fact>]
let ``A structure with no members is a value-equal class`` () = Assert.Equal(PingInput(), PingInput())
