module NSmithy.FSharp.Tests.TypeTests

open Example.Kitchen
open Xunit

[<Fact>]
let ``A string enum maps a value it does not define to Unknown`` () =
    Assert.Equal(Ingredient.CHEESE, Ingredient.FromValue "cheese")
    Assert.Equal(Ingredient.Unknown "pineapple", Ingredient.FromValue "pineapple")
    Assert.Equal("secret", Ingredient.SECRETSAUCE.Value)

[<Fact>]
let ``Members whose names clash with generated or inherited names take a suffix`` () =
    let receipt = Receipt.UnknownCase "a case named unknown"
    let error = OutOfStock(Ingredient.BACON, dataValue = "d")
    Assert.Equal(Some "d", error.DataValue)
    Assert.NotEqual(Receipt.Pending "a case named unknown", receipt)

[<Fact>]
let ``An error takes its optional members as optional arguments and applies defaults`` () =
    let error = OutOfStock(Ingredient.BACON, errorMessage = "gone")
    Assert.Equal("gone", error.Message)
    Assert.Equal(Some "gone", error.ErrorMessage)
    Assert.Equal(5, error.RestockDays)
    Assert.Equal(None, OutOfStock(Ingredient.BACON).ErrorMessage)

[<Fact>]
let ``A retryable error implements ISmithyRetryableError`` () =
    let error: exn = Throttled("slow down")
    match box error with
    | :? NSmithy.Core.ISmithyRetryableError as retryable -> Assert.True(retryable.IsThrottlingError)
    | _ -> Assert.Fail "Throttled is not retryable"

[<Fact>]
let ``A structure with no members is a value-equal class`` () =
    Assert.Equal(PingInput(), PingInput())
