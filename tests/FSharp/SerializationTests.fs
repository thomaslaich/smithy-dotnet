module NSmithy.FSharp.Tests.SerializationTests

open System
open System.Text
open Example.Kitchen
open Example.Kitchen.Schemas
open Example.Shared
open NSmithy.Codecs.Cbor
open NSmithy.Codecs.Json
open NSmithy.Core
open NSmithy.Core.Serde
open Xunit

let private dish =
    { Name = "pizza"
      Ingredients = [ Ingredient.BACON; Ingredient.FromValue "pineapple" ]
      Sides =
        Some
            { Name = "salad"
              Ingredients = []
              Sides = None
              Price = 2.0f
              Spice = Some Spice.HOT
              Tags = None
              Extras = []
              Blob = None
              MadeAt = None
              Doc = None
              Big = None
              Dec = None
              Offset = 0L
              Type = None
              ToStringValue = None }
      Price = 9.5f
      Spice = None
      Tags = Some(Map [ "a", Some 1; "b", None ])
      Extras = [ "x" ]
      Blob = Some [| 1uy; 2uy |]
      MadeAt = Some(DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.Zero))
      Doc = None
      Big = Some 12345678901234567890I
      Dec = Some 1.25M
      Offset = -3L
      Type = Some "t"
      ToStringValue = Some "s" }

let private input =
    { Dish = dish
      Notes = Some [ Some "n"; None ]
      Seating = Seating.BAR
      Guests = 4
      Tip = Some { Amount = 2.5M; Currency = "EUR" } }

let private roundtrip (factory: ICodecFactory) (schema: Schema<'T>) (value: 'T) =
    let codec = factory.FromSchema(schema)
    codec.Deserialize(codec.Serialize value)

let factories: obj[] list =
    [ [| JsonCodecFactory.Default :> ICodecFactory |]
      [| CborCodecFactory.Default :> ICodecFactory |] ]

[<Theory>]
[<MemberData(nameof factories)>]
let ``Records, lists, sparse collections, maps, and nested namespaces round-trip`` (factory: ICodecFactory) =
    Assert.Equal(input, roundtrip factory OrderInputSchema.Schema input)

[<Theory>]
[<MemberData(nameof factories)>]
let ``Every union case round-trips, including a Unit case`` (factory: ICodecFactory) =
    for receipt in
        [ Receipt.Paid { Amount = 1M; Currency = "CHF" }
          Receipt.Comped
          Receipt.Pending "p"
          Receipt.UnknownCase "u" ] do
        Assert.Equal(receipt, roundtrip factory ReceiptSchema.Schema receipt)

[<Theory>]
[<MemberData(nameof factories)>]
let ``Errors round-trip`` (factory: ICodecFactory) =
    let error =
        roundtrip factory OutOfStockSchema.Schema (OutOfStock(Ingredient.CHEESE, errorMessage = "gone"))

    Assert.Equal(Ingredient.CHEESE, error.Item)
    Assert.Equal(Some "gone", error.ErrorMessage)
    Assert.Equal(5, error.RestockDays)

[<Fact>]
let ``Documents and timestamp formats reach the wire`` () =
    let codec = JsonCodecFactory.Default.FromSchema(DishSchema.Schema)

    let json =
        Encoding.UTF8.GetString(
            codec.Serialize
                { dish with
                    Doc = Some(Document.From "doc") }
        )

    Assert.Contains("\"madeAt\":\"2024-01-02T03:04:05Z\"", json)
    Assert.Contains("\"doc\":\"doc\"", json)
    Assert.Contains("\"toString\":\"s\"", json)

[<Fact>]
let ``Reading fills in modeled defaults`` () =
    let codec = JsonCodecFactory.Default.FromSchema(OrderInputSchema.Schema)

    let read =
        codec.Deserialize(Encoding.UTF8.GetBytes """{"dish":{"name":"x","ingredients":["cheese","secret"]}}""")

    Assert.Equal(Seating.TABLE, read.Seating)
    Assert.Equal(2, read.Guests)
    Assert.Equal(1.5f, read.Dish.Price)
    Assert.Equal(-3L, read.Dish.Offset)
    Assert.Equal<string list>([], read.Dish.Extras)
    Assert.Equal(None, read.Tip)
    Assert.Equal<Ingredient list>([ Ingredient.CHEESE; Ingredient.SECRETSAUCE ], read.Dish.Ingredients)

[<Fact>]
let ``Reading a structure without a required member fails`` () =
    let codec = JsonCodecFactory.Default.FromSchema(OrderInputSchema.Schema)

    Assert.Throws<MissingRequiredMemberException>(fun () ->
        codec.Deserialize(Encoding.UTF8.GetBytes """{"dish":{"name":"x"}}""") |> ignore)
    |> ignore
