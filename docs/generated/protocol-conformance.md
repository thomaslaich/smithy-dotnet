# Official Protocol Conformance Matrix

| Protocol | Case kind | Executable | Skipped | Total | Conformance |
| --- | ---: | ---: | ---: | ---: | ---: |
| `alloy#simpleRestJson` | `Request` | 12 | 11 | 23 | 52.2% |
| `alloy#simpleRestJson` | `Response` | 7 | 13 | 20 | 35.0% |
| `aws.protocols#restJson1` | `Request` | 5 | 153 | 158 | 3.2% |
| `aws.protocols#restJson1` | `Response` | 4 | 110 | 114 | 3.5% |
| `aws.protocols#restJson1` | `MalformedRequest` | 0 | 191 | 191 | 0.0% |

## Executable Cases

- `alloy#simpleRestJson` `Request` `AddMenuItem`
- `alloy#simpleRestJson` `Request` `CustomCodeInput`
- `alloy#simpleRestJson` `Request` `GetEnumInput`
- `alloy#simpleRestJson` `Request` `GetIntEnumInput`
- `alloy#simpleRestJson` `Request` `GetMenuRequest`
- `alloy#simpleRestJson` `Request` `HeaderEndpointInput`
- `alloy#simpleRestJson` `Request` `HealthGet`
- `alloy#simpleRestJson` `Request` `RoundTripRequest`
- `alloy#simpleRestJson` `Request` `RoutingAbc`
- `alloy#simpleRestJson` `Request` `RoutingAbcDef`
- `alloy#simpleRestJson` `Request` `RoutingAbcLabel`
- `alloy#simpleRestJson` `Request` `RoutingAbcXyz`
- `alloy#simpleRestJson` `Response` `AddMenuItemResult`
- `alloy#simpleRestJson` `Response` `CustomCodeOutput`
- `alloy#simpleRestJson` `Response` `GetEnumOutput`
- `alloy#simpleRestJson` `Response` `GetIntEnumOutput`
- `alloy#simpleRestJson` `Response` `GetMenuResponse`
- `alloy#simpleRestJson` `Response` `VersionOutput`
- `alloy#simpleRestJson` `Response` `headerEndpointResponse`
- `aws.protocols#restJson1` `Request` `HttpQueryParamsOnlyRequest`
- `aws.protocols#restJson1` `Request` `RestJsonConstantQueryString`
- `aws.protocols#restJson1` `Request` `RestJsonEmptyInputAndEmptyOutput`
- `aws.protocols#restJson1` `Request` `RestJsonHttpPayloadWithStructure`
- `aws.protocols#restJson1` `Request` `RestJsonHttpPrefixHeadersArePresent`
- `aws.protocols#restJson1` `Response` `RestJsonHttpPayloadWithStructure`
- `aws.protocols#restJson1` `Response` `RestJsonHttpPrefixHeadersArePresent`
- `aws.protocols#restJson1` `Response` `RestJsonHttpResponseCode`
- `aws.protocols#restJson1` `Response` `RestJsonHttpResponseCodeWithNoPayload`
