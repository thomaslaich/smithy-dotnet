using RpcV2Json.Conformance;

namespace RpcV2.Conformance;

/// <summary>The format-specific parts of this suite's shared RPC v2 harness.</summary>
internal static class ConformanceProtocol
{
    public const string Name = "RpcV2Json";

    public static byte[] DecodeBody(string? body) =>
        string.IsNullOrEmpty(body) ? [] : System.Text.Encoding.UTF8.GetBytes(body);

    public static void AssertBody(string? expected, byte[] actual)
    {
        if (string.IsNullOrEmpty(expected) && actual.Length == 0)
            return;

        JsonAssert.AreStructurallyEqual(expected ?? "", actual);
    }
}
