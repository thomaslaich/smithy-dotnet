---
title: Validation
description: Supported input constraints, malformed requests, and validation errors.
---

The server checks supported constraints on deserialized inputs before invoking
the handler. HTTP bindings also reject malformed bodies and incompatible media
types:

| What is wrong | Status | Error type |
| --- | --- | --- |
| A value breaks a constraint trait | 400 | `ValidationException` |
| The bytes are not the shape the model declares | 400 | `SerializationException` |
| The `Content-Type` is not the one the operation reads | 415 | `UnsupportedMediaTypeException` |
| The `Accept` excludes the response's media type | 406 | `NotAcceptableException` |

## Constraint traits

### What is checked

| Trait | Applies to |
| --- | --- |
| `@required` | any member |
| `@length` | string, blob, list, set, map |
| `@range` | byte, short, integer, long, float, double, bigInteger, bigDecimal |
| `@pattern` | string |
| `@uniqueItems` | list |

Enum membership is checked as well. Generated enum types stay open on the wire so
a client is not broken by a server that adds a member; the server is where that
openness stops, and a value outside the modeled set is rejected. This covers both
an enum shape and a string carrying the deprecated `@enum` trait — the latter
generates a plain `string`, and its value set is read from the trait. A value the
model marks `@internal` is accepted but left out of the message, so rejecting a
request does not advertise it.

Constraints are enforced wherever they sit — on a member, on the shape a member
targets, on a list's elements, or on a map's keys or values — and validation
recurses through structures, lists, maps, and unions.

### The response

A violation produces `smithy.framework#ValidationException` with HTTP 400.
Every operation carries this error implicitly, so a generated client
deserializes it as a modeled `ValidationException` whether or not the model
declares it.

```json
{
  "message": "2 validation errors detected. Value with length 2 at '/slug' failed to satisfy constraint: Member must have length between 3 and 10, inclusive; Value at '/age' failed to satisfy constraint: Member must be between 1 and 100, inclusive",
  "fieldList": [
    {
      "path": "/slug",
      "message": "Value with length 2 at '/slug' failed to satisfy constraint: Member must have length between 3 and 10, inclusive"
    },
    {
      "path": "/age",
      "message": "Value at '/age' failed to satisfy constraint: Member must be between 1 and 100, inclusive"
    }
  ]
}
```

The top-level `message` summarizes the entries in `fieldList`.

Validation reports every violation it finds rather than stopping at the first,
and a member that breaks two constraints produces two entries: `@length` and
`@pattern` on the same string are separate checks with separate messages.

### Paths

`path` is a [JSONPointer](https://www.rfc-editor.org/rfc/rfc6901) into the input.
The root is the empty string, and `~` and `/` inside a name are escaped as `~0`
and `~1`.

| Where the violation is | Path |
| --- | --- |
| structure member | `/name` |
| nested member | `/address/street` |
| list element | `/tags/0` |
| map value | `/labels/env` |
| map key | the map itself — `/labels` |
| union case | `/contact/email` |

A map key is reported at the map because the key is not a value sitting at the
entry's pointer; the entry's value is.

### Missing members

A `@required` member that arrives as null fails validation like any other
constraint. A member missing from the payload entirely never reaches the
validator — deserialization fails first — but the runtime recognises that and
answers with the same modeled response rather than a server fault, using the
same wording:

```
Value at '/name' failed to satisfy constraint: Member must not be null
```

### Event streams

The initial input of a streaming operation is validated before the handler runs.
Individual events are not validated.

### Cost

A validator is compiled once per operation from the schema, when the service
starts, and reused for every request. An operation whose input carries no
constraints anywhere reachable gets no validator at all and skips validation
entirely.

## Unreadable input

Malformed bodies, invalid numeric or timestamp representations, invalid base64,
null elements in dense lists, and unions with multiple members fail during
deserialization. HTTP bindings return 400 with `SerializationException`:

```json
{ "message": "Expected an integer but found \"10\"." }
```

Server and client codecs use separate parsing rules where the protocol requires
it. For example, servers reject UTC offsets in `date-time` timestamps where
clients accept them.

## Content negotiation

The model fixes one media type per operation: the body codec's for a structured
body, the `@mediaType` or the implied type for an `@httpPayload` member. A request
body that arrives as anything else is answered with 415
`UnsupportedMediaTypeException`, and so is a body sent to an operation that reads
none.

Two cases are deliberately unconstrained. A blob payload with no `@mediaType`
carries bytes the protocol assigns no meaning to, so any `Content-Type` is
accepted. And alloy's `simpleRestJson` does not require a body to declare its
media type at all, because its own protocol tests send JSON bodies without one;
AWS's REST protocols do.

An `Accept` header that excludes the response's media type is answered with 406
`NotAcceptableException`. `*/*` and `type/*` match, an absent header constrains
nothing, and an operation with no modeled output has no media type to negotiate.

## Clients do not validate

Generated clients send inputs without constraint validation and deserialize the
server's `ValidationException` as a modeled error. Validation remains on the
server, where it uses the deployed contract.

## Not covered

- `@length` on a `@streaming` blob is not enforced, since the stream reaches the
  handler unread.
- Traits outside the constraint set, such as `@idRef` reference resolution.

See [Known Limitations](/smithy-dotnet/reference/known-limitations/).
