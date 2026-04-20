# Official Protocol Conformance Matrix

| Protocol | Case kind | Executable | Skipped | Total | Conformance |
| --- | ---: | ---: | ---: | ---: | ---: |
| `alloy#simpleRestJson` | `Request` | 5 | 18 | 23 | 21.7% |
| `alloy#simpleRestJson` | `Response` | 1 | 19 | 20 | 5.0% |
| `aws.protocols#restJson1` | `Request` | 2 | 156 | 158 | 1.3% |
| `aws.protocols#restJson1` | `Response` | 1 | 113 | 114 | 0.9% |
| `aws.protocols#restJson1` | `MalformedRequest` | 0 | 191 | 191 | 0.0% |

## Executable Cases

- `alloy#simpleRestJson` `Request` `HealthGet`
- `alloy#simpleRestJson` `Request` `RoutingAbc`
- `alloy#simpleRestJson` `Request` `RoutingAbcDef`
- `alloy#simpleRestJson` `Request` `RoutingAbcLabel`
- `alloy#simpleRestJson` `Request` `RoutingAbcXyz`
- `alloy#simpleRestJson` `Response` `headerEndpointResponse`
- `aws.protocols#restJson1` `Request` `RestJsonEmptyInputAndEmptyOutput`
- `aws.protocols#restJson1` `Request` `RestJsonHttpPrefixHeadersArePresent`
- `aws.protocols#restJson1` `Response` `RestJsonHttpPrefixHeadersArePresent`
