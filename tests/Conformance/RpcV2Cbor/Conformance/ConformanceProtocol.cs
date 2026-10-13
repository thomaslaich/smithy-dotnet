using RpcV2Cbor.Conformance;

namespace RpcV2.Conformance;

/// <summary>The format-specific parts of this suite's shared RPC v2 harness.</summary>
internal static class ConformanceProtocol
{
    public const string Name = "RpcV2Cbor";

    public static byte[] DecodeBody(string? body) =>
        string.IsNullOrEmpty(body) ? [] : Convert.FromBase64String(body);

    public static void AssertBody(string? expected, byte[] actual)
    {
        if (string.IsNullOrEmpty(expected) && actual.Length == 0)
            return;

        CborAssert.AreStructurallyEqual(DecodeBody(expected), actual);
    }
}
