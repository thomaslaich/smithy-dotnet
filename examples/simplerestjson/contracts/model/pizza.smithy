$version: "2"

namespace alloy.test

use alloy#simpleRestJson
use alloy#jsonUnknown
use alloy#discriminated
use alloy#preserveKeyOrder
use smithy.api#httpApiKeyAuth

/// A pizza restaurant administration service.
///
/// Demonstrates the `alloy#simpleRestJson` protocol with unions, enums,
/// custom HTTP bindings, pagination, and primitive type encodings.
@simpleRestJson
@httpApiKeyAuth(name: "X-Api-Key", in: "header")
service PizzaAdminService {
    version: "1.0.0",
    errors: [GenericServerError, GenericClientError],
    operations: [AddMenuItem, GetMenu, Version, Health, AuthenticatedHealth, HeaderEndpoint, RoundTrip, GetEnum, GetIntEnum, CustomCode, HttpPayloadWithDefault, HttpPayloadRequiredWithDefault, OpenUnions, Primitives, PreserveOrder]
}

/// Adds an item to a restaurant's menu.
@http(method: "POST", uri: "/restaurant/{restaurant}/menu/item", code: 201)
operation AddMenuItem {
    input: AddMenuItemInput,
    errors: [PriceError],
    output: AddMenuItemOutput
}

/// Returns the full menu for a restaurant.
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

/// Returns the string value of a `TheEnum` member.
@readonly
@http(method: "GET", uri: "/get-enum/{aa}", code: 200)
operation GetEnum {
    input: GetEnumInput,
    output: GetEnumOutput,
    errors: [ UnknownServerError ]
}

/// Returns a response with a custom HTTP status code.
@readonly
@http(method: "GET", uri: "/custom-code/{code}", code: 200)
operation CustomCode {
    input: CustomCodeInput,
    output: CustomCodeOutput,
    errors: [ UnknownServerError ]
}

/// Returns the current service version.
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
    /// The ID assigned to the newly created menu item.
    @httpPayload
    @required
    itemId: String,
    /// The time at which the item was added, as Unix epoch seconds.
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

/// Returned when a menu item has an invalid price.
@error("client")
structure PriceError {
    @required
    message: String,
    /// Machine-readable error code returned in the `X-CODE` response header.
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

/// Returned when the requested restaurant does not exist.
@error("client")
@httpError(404)
structure NotFoundError {
    @required
    name: String
}

/// Catch-all client error.
@error("client")
structure FallbackError {
    @required
    error: String
}

/// A restaurant menu, mapping item IDs to menu items.
map Menu {
    key: String,
    value: MenuItem
}

/// A single item on the menu.
structure MenuItem {
    @required
    food: Food,
    /// Price in the local currency.
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

/// The sauce base for a pizza.
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

/// Unhandled server error.
@error("server")
@httpError(502)
structure GenericServerError {
    @required
    message: String
}

/// Unhandled client error.
@error("client")
@httpError(418)
structure GenericClientError {
    @required
    message: String
}

/// Returns the health status of the service.
@readonly
@http(method: "GET", uri: "/health", code: 200)
operation Health {
    input: HealthInput,
    output: HealthOutput,
    errors: [ UnknownServerError ]
}

/// Returns health status only when the configured client API key is present.
@readonly
@http(method: "GET", uri: "/authenticated-health", code: 200)
operation AuthenticatedHealth {
    input := {
        @httpHeader("X-Api-Key")
        apiKey: String
    }
    output := {
        @required
        status: String
    },
    errors: [ UnauthorizedError ]
}

@input
structure HealthInput {
    /// Optional query string (max 5 characters) for diagnostic use.
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

/// Unhandled server error with an error code and optional diagnostic fields.
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

/// Returned when an authenticated example request is missing the expected API key.
@error("client")
@httpError(401)
structure UnauthorizedError {
    @required
    message: String
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

/// A simple string enum with two values.
enum TheEnum {
    V1 = "v1"
    V2 = "v2"
}

/// Returns the integer enum value for a given member.
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

/// An integer enum with two named values.
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
    /// The HTTP status code echoed back from the request path.
    @httpResponseCode
    code: Integer
}

/// Echoes back a body string, using `"default value"` when the body is absent.
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

/// Like `HttpPayloadWithDefault`, but the body field is also `@required`.
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
///
/// Accepts an `OpenUnionsPayload` and echoes it back unchanged.
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

/// A tagged union — the variant name is the JSON key, its value is the payload.
///
/// Unknown variants are captured by the `other` member (annotated with
/// `@jsonUnknown`) and round-tripped without data loss.
union OpenTaggedUnion {
    str: String
    @jsonUnknown other: Document
}

/// A discriminated union — a `"key"` field inlined into the object identifies
/// the variant, and the remaining fields are the variant's payload.
///
/// Unknown variants are captured by the `other` member.
@discriminated("key")
union OpenDiscriminatedUnion {
    smol: SmallStruct
    @jsonUnknown other: Document
}

structure SmallStruct {
    @required content: String
}

/// Echoes back a set of alloy primitive types.
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

/// A map that preserves the insertion order of its keys in JSON.
@preserveKeyOrder
map MyMap {
    key: String,
    value: Integer
}

/// Echoes back a key-order-preserving map and a document.
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
