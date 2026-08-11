---
title: Validation
description: The server enforces the model's constraint traits before the handler runs and answers a violation with smithy.framework#ValidationException.
---

The server checks a deserialized request against the model's constraint traits
before calling the handler. A handler only sees input the model permits, so it
does not need to re-check what the model already states.

## What is checked

| Trait | Applies to |
| --- | --- |
| `@required` | any member |
| `@length` | string, blob, list, set, map |
| `@range` | byte, short, integer, long, float, double, bigInteger, bigDecimal |
| `@pattern` | string |
| `@uniqueItems` | list |

Enum membership is checked as well. Generated enum types stay open on the wire so
a client is not broken by a server that adds a member; the server is where that
openness stops, and a value outside the modeled set is rejected.

Constraints are enforced wherever they sit — on a member, on the shape a member
targets, on a list's elements, or on a map's keys or values — and validation
recurses through structures, lists, maps, and unions.

## The response

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

## Paths

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

## Missing members

A `@required` member that arrives as null fails validation like any other
constraint. A member missing from the payload entirely never reaches the
validator — deserialization fails first — but the runtime recognises that and
answers with the same modeled response rather than a server fault, using the
same wording:

```
Value at '/name' failed to satisfy constraint: Member must not be null
```

## Event streams

For a streaming operation the initial request is validated exactly as a unary
input is: an event stream changes how the body is framed, not what the input
structure has to satisfy. The events themselves are not validated — rejecting
one mid-stream would mean reporting a violation after the response has already
begun.

## Cost

A validator is compiled once per operation from the schema, when the service
starts, and reused for every request. An operation whose input carries no
constraints anywhere reachable gets no validator at all and skips validation
entirely.

Only servers build one. Clients take their half of the protocol from a separate
factory, so a client never compiles a validator it would not run.

## Clients do not validate

This is deliberate. The server is the authority on the contract, so checking on
the client would duplicate the check that actually decides, add latency to every
call, and go stale as soon as the model changes without a client rebuild. A
generated client sends what it is given and surfaces the server's
`ValidationException` as a modeled error.

## Not covered

- Input the codec cannot parse at all — a non-numeric integer, an unparseable
  timestamp, a body that is not JSON — surfaces as a 500 rather than the
  structured 400 Smithy specifies.
- The legacy `@enum` trait on a string is not validated; only enum *shapes* are.
- `@length` on a `@streaming` blob is not enforced, since the stream reaches the
  handler unread.
- Traits outside the constraint set, such as `@idRef` reference resolution.

See [Known Limitations](/smithy-dotnet/reference/known-limitations/).
