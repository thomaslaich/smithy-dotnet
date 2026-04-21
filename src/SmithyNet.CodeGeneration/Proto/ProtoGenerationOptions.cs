namespace SmithyNet.CodeGeneration.Proto;

public sealed record ProtoGenerationOptions(
    string? BaseNamespace = null,
    string CSharpNamespaceSuffix = "Grpc",
    IReadOnlyList<string>? GeneratedNamespaces = null
);
