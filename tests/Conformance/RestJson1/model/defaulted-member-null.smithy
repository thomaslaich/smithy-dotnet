$version: "2"

namespace restjsonone.local

use aws.protocols#restJson1
use smithy.test#httpResponseTests

/// Local harness covering a case the official restJson1 protocol tests do not:
/// what an explicit `null` on the wire means for a member carrying `@default`.
///
/// Smithy guarantees such a member always has a value, so `null` must resolve to
/// the modelled default rather than leaving the member unset. NSmithy's structure
/// reader previously skipped it, producing an object the model says cannot exist,
/// and the whole 1,203-test suite passed either way — which is why this harness
/// exists.
@restJson1
service DefaultedMemberNullHarness {
    version: "1"
    operations: [
        GetDefaultedMembers
    ]
}

@readonly
@http(method: "GET", uri: "/defaulted-members", code: 200)
@httpResponseTests([
    {
        id: "RestJsonLocalDefaultedMembersExplicitNull"
        appliesTo: "client"
        documentation: """
            An explicit null resolves to the modelled default, exactly as an absent
            member does. Client-direction only: a server handed count=7 serializes
            it, so it could never emit this body."""
        protocol: restJson1
        code: 200
        body: """
            {"nested":{"count":null,"label":null,"enabled":null}}"""
        bodyMediaType: "application/json"
        headers: { "Content-Type": "application/json" }
        params: { nested: { count: 7, label: "fallback", enabled: true } }
    }
    {
        id: "RestJsonLocalDefaultedMembersAbsent"
        appliesTo: "client"
        documentation: """
            The absent case, asserted alongside the null case so the two can never
            silently diverge. Client-direction only, for the same reason."""
        protocol: restJson1
        code: 200
        body: """
            {"nested":{}}"""
        bodyMediaType: "application/json"
        headers: { "Content-Type": "application/json" }
        params: { nested: { count: 7, label: "fallback", enabled: true } }
    }
    {
        id: "RestJsonLocalDefaultedMembersPresent"
        documentation: """
            A present value still wins over the default, so the fix cannot be
            mistaken for unconditionally overwriting members."""
        protocol: restJson1
        code: 200
        body: """
            {"nested":{"count":42,"label":"explicit","enabled":false}}"""
        bodyMediaType: "application/json"
        headers: { "Content-Type": "application/json" }
        params: { nested: { count: 42, label: "explicit", enabled: false } }
    }
])
operation GetDefaultedMembers {
    output := {
        @required
        nested: DefaultedMembers
    }
}

/// Deliberately nested rather than inlined into the operation output.
///
/// A REST operation's top-level output is read by the projection reader, because
/// its members split between the body and the HTTP envelope. Only a nested
/// structure exercises the structure *value* reader, which is where the bug lived
/// — inlining these members into the output makes all three cases pass against
/// the buggy code.
structure DefaultedMembers {
    @default(7)
    count: Integer

    @default("fallback")
    label: String

    @default(true)
    enabled: Boolean
}
