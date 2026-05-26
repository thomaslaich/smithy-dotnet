$version: "2"

namespace alloy.test

use alloy#simpleRestJson
use alloy#jsonUnknown
use alloy#discriminated
use alloy#preserveKeyOrder

@simpleRestJson
service PizzaAdminService {
    version: "1.0.0",
    errors: [GenericServerError, GenericClientError],
    operations: [AddMenuItem, GetMenu, Version, Health, HeaderEndpoint, RoundTrip, GetEnum, GetIntEnum, CustomCode, HttpPayloadWithDefault, HttpPayloadRequiredWithDefault, OpenUnions, Primitives, PreserveOrder]
}

@http(method: "POST", uri: "/restaurant/{restaurant}/menu/item", code: 201)
operation AddMenuItem {
    input: AddMenuItemInput,
    errors: [PriceError],
    output: AddMenuItemOutput
}

@readonly
@http(method: "GET", uri: "/restaurant/{restaurant}/menu", code: 200)
operation GetMenu {
    input: GetMenuInput,
    errors: [NotFoundError, FallbackError],
    output: GetMenuOutput
}

/// Echoes back all HTTP headers that were sent in the request.
@http(method: "POST", uri: "/headers/", code: 200)
operation HeaderEndpoint {
    input := {
        @httpHeader("X-UPPERCASE-HEADER")
        uppercaseHeader: String,
        @httpHeader("X-Capitalized-Header")
        capitalizedHeader: String,
        @httpHeader("x-lowercase-header")
        lowercaseHeader: String,
        @httpHeader("x-MiXeD-hEaDEr")
        mixedHeader: String,
    }
    output := {
        @httpHeader("X-UPPERCASE-HEADER")
        uppercaseHeader: String,
        @httpHeader("X-Capitalized-Header")
        capitalizedHeader: String,
        @httpHeader("x-lowercase-header")
        lowercaseHeader: String,
        @httpHeader("x-MiXeD-hEaDEr")
        mixedHeader: String,
    }
}

/// Echoes back the label, header, query parameter, and body that were sent.
@http(method: "POST", uri: "/roundTrip/{label}", code: 200)
operation RoundTrip {
    input := {
        @httpLabel
        @required
        label: String,
        @httpHeader("HEADER")
        header: String,
        @httpQuery("query")
        query: String,
        body: String
    }
    output := {
        label: String,
        header: String,
        query: String,
        body: String
    }
}

@readonly
@http(method: "GET", uri: "/get-enum/{aa}", code: 200)
operation GetEnum {
    input: GetEnumInput,
    output: GetEnumOutput,
    errors: [ UnknownServerError ]
}

@readonly
@http(method: "GET", uri: "/custom-code/{code}", code: 200)
operation CustomCode {
    input: CustomCodeInput,
    output: CustomCodeOutput,
    errors: [ UnknownServerError ]
}

@readonly
@http(method: "GET", uri: "/version", code: 200)
operation Version {
    output: VersionOutput
}

@input
structure AddMenuItemInput {
    @httpLabel
    @required
    restaurant: String,
    @httpPayload
    @required
    menuItem: MenuItem
}

@output
structure AddMenuItemOutput {
    @httpPayload
    @required
    itemId: String,
    @timestampFormat("epoch-seconds")
    @httpHeader("X-ADDED-AT")
    @required
    added: Timestamp
}

@output
structure VersionOutput {
    @httpPayload
    @required
    version: String
}

@error("client")
structure PriceError {
    @required
    message: String,
    @required
    @httpHeader("X-CODE")
    code: Integer
}

@input
structure GetMenuInput {
    @httpLabel
    @required
    restaurant: String
}

@output
structure GetMenuOutput {
    @required
    @httpPayload
    menu: Menu
}

@error("client")
@httpError(404)
structure NotFoundError {
    @required
    name: String
}

@error("client")
structure FallbackError {
    @required
    error: String
}

map Menu {
    key: String,
    value: MenuItem
}

structure MenuItem {
    @required
    food: Food,
    @required
    price: Float
}

/// A food item — either a pizza or a salad.
union Food {
    pizza: Pizza,
    salad: Salad
}

structure Salad {
    @required
    name: String,
    @required
    ingredients: Ingredients
}

structure Pizza {
    @required
    name: String,
    @required
    base: PizzaBase,
    @required
    toppings: Ingredients
}

enum PizzaBase {
    CREAM = "C"
    TOMATO = "T"
}

enum Ingredient {
    TOMATO = "TOMATO"
    CHEESE = "CHEESE"
    PINEAPPLE = "PINEAPPLE"
    BACON = "BACON"
    CHICKEN = "CHICKEN"
    SALAD = "Salad"
    MUSHROOM = "MUSHROOM"
    OLIVES = "OLIVES"
    ONIONS = "ONIONS"
    PEPPERONI = "PEPPERONI"
    PEPPERS = "PEPPERS"
}

list Ingredients {
    member: Ingredient
}

@error("server")
@httpError(502)
structure GenericServerError {
    @required
    message: String
}

@error("client")
@httpError(418)
structure GenericClientError {
    @required
    message: String
}

@readonly
@http(method: "GET", uri: "/health", code: 200)
operation Health {
    input: HealthInput,
    output: HealthOutput,
    errors: [ UnknownServerError ]
}

@input
structure HealthInput {
    @httpQuery("query")
    @length(min: 0, max: 5)
    query: String
}

@freeForm(i: 1, a: 2)
@output
structure HealthOutput {
    @required
    status: String
}

@error("server")
@httpError(500)
structure UnknownServerError {
    @required
    errorCode: UnknownServerErrorCode,

    description: String,

    stateHash: String
}

enum UnknownServerErrorCode {
    ERROR_CODE = "server.error",
}


@trait
document freeForm


@input
structure GetEnumInput {
    @required
    @httpLabel
    aa: TheEnum
}

@output
structure GetEnumOutput {
    result: String
}

enum TheEnum {
    V1 = "v1"
    V2 = "v2"
}


@readonly
@http(method: "GET", uri: "/get-int-enum/{aa}", code: 200)
operation GetIntEnum {
    input := {
        @required
        @httpLabel
        aa: EnumResult
    }
    output := {
        @required
        result: EnumResult
    }
    errors: [ UnknownServerError ]
}

intEnum EnumResult {
    FIRST = 1
    SECOND = 2
}

@input
structure CustomCodeInput {
    @httpLabel
    @required
    code: Integer
}

@output
structure CustomCodeOutput {
    @httpResponseCode
    code: Integer
}

@idempotent
@http(uri: "/httpPayloadWithDefault", method: "PUT")
operation HttpPayloadWithDefault {
    input := {
        @httpPayload
        @default("default value")
        body: String,
    }
    output := {
        @httpPayload
        @default("default value")
        body: String,
    }
}

@idempotent
@http(uri: "/httpPayloadRequiredWithDefault", method: "PUT")
operation HttpPayloadRequiredWithDefault {
    input := {
        @httpPayload
        @default("default value")
        @required
        body: String
    }
    output := {
        @httpPayload
        @default("default value")
        @required
        body: String
    }
}

/// Demonstrates tagged and discriminated open unions.
@idempotent
@http(uri: "/openUnions", method: "PUT")
operation OpenUnions {
    input := {
      @required @httpPayload data: OpenUnionsPayload
    }
    output := {
        @required @httpPayload data: OpenUnionsPayload
    }
}

/// A payload that can be either a tagged union or a discriminated union.
union OpenUnionsPayload {
    tagged: OpenTaggedUnion
    discriminated: OpenDiscriminatedUnion
}

/// A tagged union — the variant name is the key, its value is the payload.
union OpenTaggedUnion {
    str: String
    @jsonUnknown other: Document
}

/// A discriminated union — the discriminator field (`key`) is inlined alongside payload fields.
@discriminated("key")
union OpenDiscriminatedUnion {
    smol: SmallStruct
    @jsonUnknown other: Document
}

structure SmallStruct {
    @required content: String
}

@http(uri: "/primitive/encoding", method: "POST", code: 200)
operation Primitives {
    input := {
        @required
        uuid: alloy#UUID
        @required
        localDate: alloy#LocalDate
        @required
        localTime: alloy#LocalTime
        @required
        duration: alloy#Duration
        @required
        offsetDateTime: alloy#OffsetDateTime
    }
    output := {
        @required
        uuid: alloy#UUID
        @required
        localDate: alloy#LocalDate
        @required
        localTime: alloy#LocalTime
        @required
        duration: alloy#Duration
        @required
        offsetDateTime: alloy#OffsetDateTime
    }
}

@preserveKeyOrder
map MyMap {
    key: String,
    value: Integer
}

@http(uri: "/preserveKeyOrder", method: "POST", code: 200)
operation PreserveOrder {
    input := {
        map: MyMap
        @preserveKeyOrder
        document: Document
    }
    output := {
        map: MyMap
        @preserveKeyOrder
        document: Document
    }
}
