---
title: Validation
description: The server rejects a request the model does not allow before the handler runs, with the status and body Smithy specifies.
---

A handler only sees input the model permits. Everything that could make a request
something else is answered before the handler runs, and each kind of problem has
its own answer:

| What is wrong | Status | Error type |
| --- | --- | --- |
| A value breaks a constraint trait | 400 | `ValidationException` |
| The bytes are not the shape the model declares | 400 | `SerializationException` |
| The `Content-Type` is not the one the operation reads | 415 | `UnsupportedMediaTypeException` |
| The `Accept` excludes the response's media type | 406 | `NotAcceptableException` |

The rest of this page covers each in turn. The generated server is run against
Smithy's `httpMalformedRequestTests` suite for restJson1, which asserts every one
of these; see [Protocol Status](/smithy-dotnet/protocols/status/).

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

The top-level `message` repeats every field message, joined with `; `, after the
count. This wording is what Smithy's malformed-request conformance tests assert,
so a caller — or a generic client — reads the same text it would get from any
other Smithy server. The generated server is run against that suite.

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

For a streaming operation the initial request is validated exactly as a unary
input is: an event stream changes how the body is framed, not what the input
structure has to satisfy. The events themselves are not validated — rejecting
one mid-stream would mean reporting a violation after the response has already
begun.

### Cost

A validator is compiled once per operation from the schema, when the service
starts, and reused for every request. An operation whose input carries no
constraints anywhere reachable gets no validator at all and skips validation
entirely.

Only servers build one. Clients take their half of the protocol from a separate
factory, so a client never compiles a validator it would not run.

## Unreadable input

A constraint violation is a value the model does not allow. A request can also
fail earlier than that, on bytes that never become a value at all: a body that is
not JSON, a non-numeric integer, a number outside its type's range, a timestamp in
a format the member does not use, a blob that is not base64, a dense list holding
`null`, a union with two members set. None of these reach the validator, because
there is nothing to validate.

They are still the caller's mistake rather than a server fault, so they are
answered with a 400 carrying `SerializationException`:

```json
{ "message": "Expected an integer but found \"10\"." }
```

A server reads by exactly what the model declares. A client is more forgiving of
the same wire — a real service may be looser than the spec, and a response it can
understand is worth reading — so the two sides compile their codecs separately.
The one rule that currently differs is the UTC offset on a `date-time` timestamp,
which Smithy's own protocol tests require a server to reject and a client to
accept.

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

This is deliberate. The server is the authority on the contract, so checking on
the client would duplicate the check that actually decides, add latency to every
call, and go stale as soon as the model changes without a client rebuild. A
generated client sends what it is given and surfaces the server's
`ValidationException` as a modeled error.

## Not covered

- `@length` on a `@streaming` blob is not enforced, since the stream reaches the
  handler unread.
- Traits outside the constraint set, such as `@idRef` reference resolution.

See [Known Limitations](/smithy-dotnet/reference/known-limitations/).
