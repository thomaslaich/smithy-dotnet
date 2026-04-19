namespace SmithyNet.Server;

public sealed record SmithyServerRequest(string ServiceName, string OperationName, object? Input);
